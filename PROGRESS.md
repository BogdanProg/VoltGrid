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
Контракт `src/VoltGrid.Contracts/Protos/station_link.proto` (`package voltgrid.station.v1`):
конверти `StationMessage` / `ServerMessage` з полем кореляції `message_id = 1` поза `oneof payload`.
Поки один варіант з кожного боку: `BootNotification` / `BootNotificationResponse` (обидва порожні).

Кодогенерація один раз у `VoltGrid.Contracts` (`GrpcServices="Both"`), Gateway посилається на проєкт.
`StationLinkService` у Gateway: `Connect` повертає `Task.CompletedTask`.

Перевірено через grpcurl на `localhost:5205` (HTTP/2 без TLS):
без перевизначення `Connect` — `Unimplemented`, з порожнім перевизначенням — OK.

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
- Перевірити, що `Station.Simulator` має `ProjectReference` на Contracts і пакет `Grpc.Net.Client` (знадобиться на 3.1)
- README: свідоме обмеження — станції не автентифікуються (ідентичність береться з з'єднання, без перевірки)