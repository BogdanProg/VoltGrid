# PROGRESS

## Поточний етап
Етап 3.1 — симулятор: одна станція, підключення, boot

## Зроблено
- [x] Етап 0.1 — скелет рішення, 7 проєктів, git
- [x] Етап 0.2 — docker-compose: postgres, redis, kafka
- [x] Етап 1.1 — домен
- [x] Етап 1.2 — `DbContext`, перша міграція, сідинг
- [x] Етап 2.1 — `.proto` контракт, кодогенерація, порожній gRPC-сервіс
- [x] Етап 2.2 — двонаправлений стрім, `BootNotification`, політика допуску, запис boot у базу
- [x] Етап 2.3 — `Heartbeat`, `StatusNotification`, допуск повідомлень, поведінка при падінні бази

## Останній робочий стан
Етап 3.1, крок 1 з 4 зроблено: `Station.Simulator` відкриває дуплексний виклик із `station-id`, надсилає один `BootNotification`, читає одну відповідь, закриває свій бік і читає фінальний статус виклику.
Перевірено:
- Gateway працює → `Accepted`, статус виклику `OK`, boot у базі;
- Gateway вимкнений → `Unavailable` на `WriteAsync` (`Connect` повертається одразу, нічого не чекаючи);
- без `station-id` → `WriteAsync` проходить, `Unauthenticated` приходить на `MoveNext`; Gateway не прочитав жодного повідомлення («надіслано» ≠ «оброблено»);
- `GetStatus()` до завершення виклику → `InvalidOperationException`; статус доступний лише після того, як `MoveNext` повернув `false`.

Контракт `station_link.proto`:
- `StationMessage.payload`: `boot_notification` = 2, `heartbeat` = 3, `status_notification` = 4;
- `ServerMessage.payload`: `boot_notification_response` = 2, `error` = 3, `heartbeat_response` = 4, `status_notification_response` = 5;
- `BootNotificationResponse`: `status`, `heartbeat_interval_seconds`, `current_time` (`google.protobuf.Timestamp`);
- `Heartbeat` порожній; `HeartbeatResponse`: `current_time`;
- `StatusNotification`: `connector_number`, `ConnectorSentStatus connector_status` (`UNSPECIFIED`, `AVAILABLE`, `OCCUPIED`, `CHARGING`, `FAULTED`, `UNAVAILABLE`); `StatusNotificationResponse` порожній;
- `ErrorResponse`: `ErrorCode code` (`UNSPECIFIED`, `NOT_ACCEPTED`, `RETRY_LATER`).

Домен:
- `LastBootStatus` (`Unknown`, `Accepted`, `Rejected`) у `ChargingStation`;
- `RecordAcceptedBoot` (прошивка, `LastBootAt`, `LastHeartbeat`, статус), `RecordRejectedBoot` (тільки статус).

`StationLinkService` у Gateway:
- `streamBootStatus` (`RegistrationStatus?`) живе весь стрім; правило допуску: boot у стрімі → його результат, інакше `LastBootStatus` у базі;
- неприйнята станція → Warning + `ErrorResponse NOT_ACCEPTED`, стрім живий;
- `RecordHeartbeatAsync` / `RecordConnectorStatusAsync`: короткий `DbContext`, перевірка допуску, запис;
- `ToDomain`: `ConnectorSentStatus` → `ConnectorStatus` через `switch`; невідоме значення → `Unknown` + Warning;
- невідомий конектор → Warning, нічого не пишемо, відповідь «отримав»;
- база лежить: boot → `Pending`; heartbeat → час (якщо допущено в стрімі) або `NOT_ACCEPTED`; статус → `RETRY_LATER` (якщо допущено в стрімі) або `NOT_ACCEPTED`.

Міграції: `InitialCreate`, `AddStationBootInfo`, `AddLastBootStatus`.
Сідер: `ST-001` (Plus, 123), `ST-002` (Pro, 456); у кожної конектори 1 (Type2) і 2 (CCS).

Перевірка (з кореня репозиторію):
grpcurl -plaintext -import-path src/VoltGrid.Contracts/Protos -proto station_link.proto -H 'station-id: ST-001' -d @ localhost:5205 voltgrid.station.v1.StationLink/Connect
{"messageId": "1", "bootNotification": {"model": "Plus", "serialNumber": "123", "firmwareVersion": "1.0.4"}}
{"messageId": "2", "heartbeat": {}}
{"messageId": "3", "statusNotification": {"connectorNumber": 2, "connectorStatus": "CONNECTOR_SENT_STATUS_OCCUPIED"}}

Наступне: етап 3.1, кроки 2–4:
2. `SendAsync`: надіслати повідомлення й дочекатися відповіді з таймаутом, перевірка `message_id`;
3. стан boot: `Accepted` / `Pending` / `Rejected`, повтор boot через `heartbeat_interval_seconds`;
4. після `Accepted` — `StatusNotification` для кожного конектора, повтор при `RETRY_LATER`.
   На 3.2: `NOT_ACCEPTED` → boot (виникає лише після перепідключення без boot); стратегія `message_id` (лічильник на процес чи `Guid.CreateVersion7()`).
   Окремий цикл читання в симуляторі — коли Gateway почне сам слати команди (етап 9.3) або коли в станції з'являться два незалежні потоки повідомлень (етап 4).

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
- README: свідоме обмеження — станції не автентифікуються (ідентичність береться з з'єднання, без перевірки)
- Вирівняти версії gRPC-пакетів (Gateway `Grpc.AspNetCore` 2.64.0 vs Contracts 2.84.0); розглянути `Grpc.AspNetCore.Server` замість метапакета
- Розглянути `WarningsAsErrors` для CA2017 у Gateway, щоб невідповідність плейсхолдерів у логах ламала збірку, а не стрім
- Два одночасні стріми з однаковим station-id: Gateway приймає обидва; вирішити на 9.1 (реєстр: новий стрім витісняє старий)
- Один збій бази дає три `fail` (EF `Database.Connection`, EF `Query` і наш Error): налаштувати рівні категорій EF на етапі 14
- Інтервал Heartbeat — константа в коді; перенести в `appsettings.json`, коли знадобиться змінювати без перезбірки
- Фільтр `NpgsqlException` у трьох `catch` ловить і помилки схеми (`PostgresException`, наприклад забута міграція), не тільки недоступність бази; звузити до `IsTransient`
- Фільтр «база недоступна» повторюється в трьох `case`; винести в один метод, коли з'явиться четверте повідомлення (етап 4)
- Конектор 0 (OCPP: статус усієї станції) не підтримується, зараз логується як невідомий конектор
- Контракт: код помилки для некоректних даних від станції (аналог OCPP `FormatViolation`)
- `RecordConnectorStatusAsync` завантажує станцію навіть тоді, коли допуск відомий зі стріму (зайвий `SELECT`)
- рівень логів категорії Grpc.AspNetCore.Server налаштувати на етапі 14