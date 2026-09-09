# PROGRESS

## Current stage
Stage 1.2 — DbContext, first migration, seeding (10.09)

## Done
- [x] Stage 0.1 — solution skeleton, 7 projects, git
- [x] Stage 0.2 — docker-compose: postgres, redis, kafka
- [x] Stage 1.1 — domain: ChargingStation, Connector, Tariff, RfidTag
  (+ ConnectorStatus, ConnectorType enums)

## Last working state
`dotnet build` is green, VoltGrid.Domain has no warnings.
Entities are immutable by default: `required` + `init`. Mutable state changes
through methods only (`RecordHeartbeat`, `ChangeStatus`, `Activate`/`Deactivate`,
`Block`/`Unblock`).
Enums have explicit numbers and `Unknown = 0`.
No navigation properties between aggregates — references by id only.

## Blocked on
—

## Technical debt
- NU1903: vulnerable Microsoft.OpenApi 2.0.0 in Charging.Api
  (transitive dependency from the webapi template)
- Program.cs in Gateway and Charging.Api is still template code, not cleaned up
- EF configurations (IEntityTypeConfiguration) not written yet — stage 1.2
- `git push` fails: GitHub rejects password auth, needs an SSH key or a token

## Deliberately deferred (not debt)
- ChargingSession — planned for stage 4.2, alongside StartTransaction
- Parsing unknown enum values at the wire/domain boundary — stage 2.2