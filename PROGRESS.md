# PROGRESS

## Поточний етап
Етап 2.3 — `Heartbeat`, `StatusNotification`

## Зроблено
- [x] Етап 0.1 — скелет рішення, 7 проєктів, git
- [x] Етап 0.2 — docker-compose: postgres, redis, kafka
- [x] Етап 1.1 — домен
- [x] Етап 1.2 — `DbContext`, перша міграція, сідинг
- [x] Етап 2.1 — `.proto` контракт, кодогенерація, порожній gRPC-сервіс
- [x] Етап 2.2 — двонаправлений стрім, `BootNotification`, політика допуску, запис boot у базу

## Останній робочий стан
Етап 2.2 закрито, усі сценарії перевірені grpcurl (рішення №4–6 у `DECISIONS.md`).

Контракт `station_link.proto`:
- `BootNotification`: `model`, `serial_number`, `firmware_version`;
- `BootNotificationResponse`: `RegistrationStatus status` (`UNSPECIFIED` = 0, `ACCEPTED`, `REJECTED`, `PENDING`) і `heartbeat_interval_seconds`.

`StationLinkService` у Gateway:
- ідентичність із заголовка `station-id`; без нього `Unauthenticated` до `try`;
- цикл `await foreach` по `ReadAllAsync`, `switch` за `PayloadCase`, відповідь з тим самим `message_id`;
- `RegisterBootAsync`: короткий `DbContext` з `IDbContextFactory`; немає в базі або інший серійний → `Rejected`; інша модель → `Accepted` + Warning; `Accepted` → `ChargingStation.RecordBoot` (прошивка, `LastBootAt`, `LastHeartbeat`) + `SaveChangesAsync`;
- база недоступна (`NpgsqlException`) → Error + `Pending`, стрім живий;
- обрив (`IOException`) → Warning; `finally` → місце для прибирання (етап 9.1);
- інтервал Heartbeat — константа 30 с.

Міграції: `InitialCreate`, `AddStationBootInfo` (`FirmwareVersion`, `LastBootAt`).
Сідер: `ST-001` (Plus, 123), `ST-002` (Pro, 456).

Перевірка (з кореня репозиторію):
grpcurl -plaintext -import-path src/VoltGrid.Contracts/Protos -proto station_link.proto -H 'station-id: ST-001' -d @ localhost:5205 voltgrid.station.v1.StationLink/Connect
{"messageId": "1", "bootNotification": {"model": "Plus", "serialNumber": "123", "firmwareVersion": "1.0.3"}}

Наступне: етап 2.3. Перше питання — що Gateway робить з повідомленнями від станції, яку ще не прийнято (до `Accepted` або після `Rejected` / `Pending`).

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
- Один збій бази дає три `fail` (EF `Database.Connection`, EF `Query` і наш Error): налаштувати рівні категорій EF на етапі 14
- Інтервал Heartbeat — константа в коді; перенести в `appsettings.json`, коли знадобиться змінювати без перезбірки