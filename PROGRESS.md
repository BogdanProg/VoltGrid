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
Етап 2.2: готові концепти 1–2 з 6 (цикл читання; диспетчеризація і відповідь).

`StationLinkService.Connect` у Gateway:
- цикл `await foreach` по `requestStream.ReadAllAsync(context.CancellationToken)`; повідомлення однієї станції обробляються строго по черзі;
- лог на кожне повідомлення з `{MessageId}` і `{PayloadCase}`;
- `switch` за `PayloadCase`: `BootNotification` → відповідь `ServerMessage` з тим самим `message_id` і порожнім `BootNotificationResponse`; `default` → Warning «Unsupported payload»;
- `catch (IOException)` → Warning «Station connection lost» одним рядком, без траси стека;
- `finally` → місце для прибирання (етап 9.1: видалити станцію з реєстру Redis).

Перевірено grpcurl з `-d @` на `localhost:5205`: штатне закриття (control+D), обрив (control+C), відповідь на boot з `messageId: "1"`.

Наступне: концепт 3 — ідентичність станції (метадані виклику чи перше повідомлення).
Відкрите питання для концепту 5: неперехоплений виняток в обробці одного повідомлення обриває весь стрім станції.

## Де застряг
—

## Технічний борг
- NU1903: вразливий Microsoft.OpenApi 2.0.0 у Charging.Api (транзитивна залежність шаблону webapi)
- Program.cs у Charging.Api — шаблонний, не чищений (у Gateway вже чистий)
- MSB3277: конфлікт версій Microsoft.EntityFrameworkCore під час збірки
- Частковий унікальний індекс «одна активна сесія на конектор» — повернутись на етапі 5.5 / 9.4
- Видалити шаблонний `Class1.cs` з `VoltGrid.Application`
- `git push` з терміналу через GitHub CLI (`gh auth login`); у `bogdannalapko` немає sudo, тож `brew install gh` з адмін-акаунта
- Контракт: варіант відповіді «не знаю такої команди» (аналог OCPP `NotImplemented`), додати разом із першою командою від Gateway
- `Station.Simulator`: `ProjectReference` на Contracts є, бракує пакета `Grpc.Net.Client` (знадобиться на 3.1)
- README: свідоме обмеження — станції не автентифікуються (ідентичність береться з з'єднання, без перевірки)
- Вирівняти версії gRPC-пакетів (Gateway `Grpc.AspNetCore` 2.64.0 vs Contracts 2.84.0); розглянути `Grpc.AspNetCore.Server` замість метапакета
- Розглянути `WarningsAsErrors` для CA2017 у Gateway, щоб невідповідність плейсхолдерів у логах ламала збірку, а не стрім