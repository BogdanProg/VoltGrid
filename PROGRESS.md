# PROGRESS

## Поточний етап
Етап 1.1 — домен (6.09)

## Зроблено
- [x] Етап 0.1 — скелет рішення, 7 проєктів, git
- [x] Етап 0.2 — docker-compose: postgres 18.6, redis 8.10.1, kafka 4.3.1 (KRaft)

## Останній робочий стан
Три контейнери healthy. Postgres перевірений з Rider (localhost:5432,
база voltgrid). Kafka: два слухачі — INTERNAL для контейнерів (kafka:9092),
EXTERNAL для процесів на macOS (localhost:9093). Томи в postgres і kafka,
redis без тому свідомо — реєстр з'єднань не переживає рестарт за задумом.

## Технічний борг
- NU1903: вразливий Microsoft.OpenApi 2.0.0 у Charging.Api
- Program.cs у Gateway і Charging.Api — шаблонні, не чищені
- Паролі у відкритому вигляді в docker-compose (свідомо, до етапу 17.8)
- PG 18 змінив PGDATA: том монтується на /var/lib/postgresql, не глибше
  (знадобиться на етапі 16.1 з Testcontainers)

## Питання на потім
- Етап 7.1 (28.09): чи йде MeterValues у подію сесії через Kafka,
  чи прямо зі стріму? Від цього залежить, чи Kafka на критичному шляху грошей.