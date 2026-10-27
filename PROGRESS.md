# PROGRESS

## Поточний етап
Етап 2.2 — двонаправлений стрім, `BootNotification`

## Зроблено
- [x] Етап 0.1 — скелет рішення, 7 проєктів, git
- [x] Етап 0.2 — docker-compose: postgres, redis, kafka
- [x] Етап 1.1 — домен
- [x] Етап 1.2 — `DbContext`, перша міграція, сідинг
- [x] Етап 2.1 — `.proto` контракт, кодогенерація, порожній gRPC-сервіс

## Останній робочий стан
Етап 2.2: готові концепти 1–3 з 6 (цикл читання; диспетчеризація і відповідь; ідентичність станції).

`StationLinkService.Connect` у Gateway:
- на старті читає заголовок `station-id` з `context.RequestHeaders`; без нього Warning з `{Peer}` і `RpcException(StatusCode.Unauthenticated)`, до `try`;
- цикл `await foreach` по `requestStream.ReadAllAsync(context.CancellationToken)`; повідомлення однієї станції обробляються строго по черзі;
- `switch` за `PayloadCase`: `BootNotification` → відповідь `ServerMessage` з тим самим `message_id` і порожнім `BootNotificationResponse`; `default` → Warning «Unsupported payload»;
- `catch (IOException)` → Warning «connection lost» одним рядком, без траси стека;
- `finally` → місце для прибирання (етап 9.1: видалити станцію з реєстру Redis);
- усі логи містять `{StationId}`.

Перевірка grpcurl (з кореня репозиторію):
grpcurl -plaintext -import-path src/VoltGrid.Contracts/Protos -proto station_link.proto -H 'station-id: ST-001' -d @ localhost:5205 voltgrid.station.v1.StationLink/Connect
Без `-H` → `Unauthenticated`. Із `-H`: штатне закриття (control+D), обрив (control+C), відповідь на boot з `messageId: "1"`.

Наступне: концепт 4 — зміст `BootNotification` і відповіді (поля, Accepted / Rejected, інтервал Heartbeat).
Відкрите питання для концепту 5: неперехоплений виняток в обробці одного повідомлення обриває весь стрім станції.

## Де застряг
—

## Технічний борг
- Central Package Management (Directory.Packages.props): одна версія кожного пакета на все рішення; закриває розбіжності EF і gRPC
- NU1903: вразливий Microsoft.OpenApi 2.0.0 у Charging.Api (транзитивна залежність шаблону webapi)
- Program.cs у Charging.Api — шаблонний, не чищений (у Gateway вже чистий)
- Частковий унікальний індекс «одна активна сесія на конектор» — повернутись на етапі 5.5 / 9.4
- Видалити шаблонний `Class1.cs` з `VoltGrid.Application`
- `git push` з терміналу через GitHub CLI (`gh auth login`); у `bogdannalapko` немає sudo, тож `brew install gh` з адмін-акаунта
- Контракт: варіант відповіді «не знаю такої команди» (аналог OCPP `NotImplemented`), додати разом із першою командою від Gateway
- `Station.Simulator`: `ProjectReference` на Contracts є, бракує пакета `Grpc.Net.Client` (знадобиться на 3.1)
- README: свідоме обмеження — станції не автентифікуються (ідентичність береться з з'єднання, без перевірки)
- Вирівняти версії gRPC-пакетів (Gateway `Grpc.AspNetCore` 2.64.0 vs Contracts 2.84.0); розглянути `Grpc.AspNetCore.Server` замість метапакета
- Розглянути `WarningsAsErrors` для CA2017 у Gateway, щоб невідповідність плейсхолдерів у логах ламала збірку, а не стрім
- Два одночасні стріми з однаковим station-id: Gateway приймає обидва; вирішити на 9.1 (реєстр: новий стрім витісняє старий)