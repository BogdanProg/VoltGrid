# PROGRESS

## Поточний етап
Етап 2.1 — .proto контракт, генерація коду, порожній gRPC-сервіс

## Зроблено
- [x] Етап 0.1 — скелет рішення, 7 проєктів, git
- [x] Етап 0.2 — docker-compose: postgres, redis, kafka
- [x] Етап 1.1 — домен: ChargingStation, Connector, Tariff, RfidTag, ChargingSession
- [x] Етап 1.2 — DbContext, конфігурації EF, перша міграція, dev-сідинг

## Останній робочий стан
Міграція InitialCreate застосована, у базі 5 таблиць.
DevDataSeeder (тільки Development) заповнює: 2 тарифи (AC/DC), 2 станції,
4 роз'єми, 3 RFID-картки (дійсна, заблокована, прострочена).
Тариф прив'язаний до роз'єму; сесія фіксує TariffId і PricePerKwh на старті.

## Де застряг
—

## Технічний борг
- NU1903: вразливий Microsoft.OpenApi 2.0.0 у Charging.Api (транзитивна залежність шаблону webapi)
- Program.cs у Gateway і Charging.Api — шаблонні, не чищені (WeatherForecast)
- Видалити шаблонний Class1.cs з VoltGrid.Application
- Частковий унікальний індекс «одна активна сесія на роз'єм» (повернутись на етапі 5.5 / 9.4)
- Міграції застосовуються вручну (dotnet ef database update) перед запуском API
- Push з терміналу: налаштувати GitHub CLI (gh auth login)