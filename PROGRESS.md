# PROGRESS

## Current stage
Stage 3.2 — simulator: heartbeat loop, reconnect with backoff

Steps:
- [x] 1. Heartbeat loop with the interval from `BootNotificationResponse`
- [x] 2. Experiment: what the station sees when Gateway dies (Ctrl+C, `kill -9`)
- [x] 3a. Reconnect loop with a fixed 5 s delay; `Unauthenticated` → exit
- [x] 3b. Reconnect without boot; `NOT_ACCEPTED` → boot again in the same stream
- [ ] 4. Exponential backoff with jitter instead of the fixed delay
- [ ] 5. `message_id` strategy, acceptance scenarios, decision 9, PROGRESS

## Done
- [x] Stage 0.1 — solution skeleton, 7 projects, git
- [x] Stage 0.2 — docker-compose: postgres, redis, kafka
- [x] Stage 1.1 — domain
- [x] Stage 1.2 — `DbContext`, first migration, seeding
- [x] Stage 2.1 — `.proto` contract, code generation, empty gRPC service
- [x] Stage 2.2 — bidirectional stream, `BootNotification`, admission policy, boot persisted to the database
- [x] Stage 2.3 — `Heartbeat`, `StatusNotification`, message admission, behaviour on database outage
- [x] Stage 3.1 — simulator: one station, `SendAsync` with timeout, boot retries, connector statuses

## Last working state
Stage 3.2, steps 1–3b are done and committed (`103703e`). The station survives a Gateway restart (graceful and `kill -9`) and a Gateway restart during a database outage, with no manual action.

`Station.Simulator` (`Program.cs`, top-level statements):
- station constants (`ST-001`, Plus, 123, 1.0.5); `ReplyTimeoutSeconds` = 35, `MinBootRetrySeconds` = 25, `MinHeartbeatIntervalSeconds` = 5, `StatusRetrySeconds` = 15, `ReconnectDelaySeconds` = 5; connectors `[1, 2]`;
- state at three levels:
    - process (survives reconnects): `lastMessageId`, `accepted` (the last boot in this process ended with `Accepted`), `heartbeatIntervalSeconds` (saved at `Accepted`);
    - call: `bootedInThisCall`, reset after every `Connect`;
    - message: everything inside `SendAsync`;
- one `GrpcChannel` and one client per process; every reconnect is a new `Connect` on the same channel;
- outer loop = one call: `Connect` → inner loop; on a reconnectable failure log one line, wait 5 s, open a new call;
- inner loop = one admission attempt in the same call: not `accepted` → `BootAsync`; statuses of all connectors (after a boot and after a reconnect without boot); `HeartbeatLoopAsync`;
- `NOT_ACCEPTED` on a status or a heartbeat → `StationNotAcceptedException`; caught only `when (!bootedInThisCall)` → `accepted = false` → boot again in the same stream (`Pending` retries happen inside `BootAsync`); after a boot in the same stream it is a protocol violation and crashes the simulator (decision 8);
- outer loop errors: `IOException` (including `HttpIOException`) and `RpcException` `Unavailable` / `Cancelled` / `Unknown` → reconnect; `Unauthenticated` → log and exit; anything else (e.g. `InvalidOperationException`) → crash with a stack trace;
- `HeartbeatLoopAsync(call, intervalSeconds)`: `Task.Delay` before each heartbeat (the first one comes an interval after boot, since boot already records `LastHeartbeat`); `HeartbeatResponse` → log the server time; never returns normally;
- `SendAsync`, `BootAsync`, `ReportStatusAsync` work as in 3.1; `ReportStatusAsync` also throws `StationNotAcceptedException` on `NOT_ACCEPTED`;
- `Log(message)`: one log format for the whole simulator, `HH:mm:ss` UTC.

Verified in 3.2:
- heartbeat every 30 s; `LastHeartbeat` updates in the database;
- `kill -9` on Gateway → the station notices at its next heartbeat (`HttpIOException` on `WriteAsync`), retries every 5 s with `Unavailable` (`Error connecting to subchannel`), and comes back by itself once Gateway is up;
- Ctrl+C on Gateway → the same, but with `IOException` (`Connection reset by peer`);
- reconnect without boot → statuses and heartbeat, no `BootNotification` in Gateway, `LastBootAt` unchanged;
- Gateway restart with Postgres stopped → status gets `NOT_ACCEPTED` → boot in the same stream → `Pending` every 30 s → Postgres started → `Accepted` → statuses → heartbeat, with no new `Connecting`; `LastBootAt` updated;
- simulator restart → boot (new process, `accepted = false`);
- `station-id` header missing → `Unauthenticated`, the simulator exits.

Findings from the 3.2 experiments:
- A connection that dies mid-stream arrives as a raw `IOException` / `HttpIOException` from the HTTP/2 layer, not as `RpcException`; only a failed connect gives `RpcException` `Unavailable`.
- The station learns that Gateway is gone only when it next touches the stream, i.e. at the next heartbeat (up to 30 s later).
- `client.Connect` is lazy: the HTTP/2 request leaves with the first `WriteAsync`. That is why `Unauthenticated` arrives on `MoveNext`, and why a log line right after `Connect` cannot claim the station is connected.
- Ctrl+C on Gateway waits for `HostOptions.ShutdownTimeout` (30 s), because `Connect` never completes on its own; streams keep working meanwhile and are then aborted all at once. Input for stage 10.5 (graceful shutdown, draining streams).
- `kill -9` skips the `finally` in `Connect`, so no cleanup runs. Once the Redis registry exists, its entry would stay behind. Input for stage 9.5 (TTL, lease).
- `message_id` gaps: the counter increments before `WriteAsync`, so failed sends consume ids (Gateway saw 5 → 13 after a restart); after a simulator restart the ids start again from 1. Input for step 5.
- With a fixed 5 s delay every station comes back within the same 5 s after Gateway recovers (thundering herd). Input for step 4.

Verified in 3.1:
- Gateway running → `Accepted`, call status `OK`, boot recorded in the database;
- Gateway stopped → `Unavailable` on `WriteAsync` (`Connect` returns immediately without waiting);
- no `station-id` → `WriteAsync` succeeds, `Unauthenticated` arrives on `MoveNext`; Gateway read no message ("sent" ≠ "processed");
- `GetStatus()` before the call ends → `InvalidOperationException`; the status is available only after `MoveNext` returned `false`;
- timeout (breakpoint in Gateway) → `Cancelled`;
- reply with `message_id` `999` → crash with both ids; Gateway sees the disconnect as `IOException` → Warning;
- serial number in the database changed to `999` → `Rejected` every 30 s (`LastBootStatus` = 2); serial restored → the next boot is `Accepted` without restarting the station (decision 5);
- Postgres stopped → `Pending` every 30 s; Postgres started → the next boot is `Accepted` (decision 6);
- after `Accepted` the statuses of both connectors are recorded (the stale `Occupied` from the grpcurl test replaced with `Available`);
- Postgres stopped between boot and statuses → `RETRY_LATER`, retry every 15 s; Postgres started → statuses recorded.

Contract `station_link.proto`:
- `StationMessage.payload`: `boot_notification` = 2, `heartbeat` = 3, `status_notification` = 4;
- `ServerMessage.payload`: `boot_notification_response` = 2, `error` = 3, `heartbeat_response` = 4, `status_notification_response` = 5;
- `BootNotificationResponse`: `status`, `heartbeat_interval_seconds`, `current_time` (`google.protobuf.Timestamp`);
- `Heartbeat` is empty; `HeartbeatResponse`: `current_time`;
- `StatusNotification`: `connector_number`, `ConnectorSentStatus connector_status` (`UNSPECIFIED`, `AVAILABLE`, `OCCUPIED`, `CHARGING`, `FAULTED`, `UNAVAILABLE`); `StatusNotificationResponse` is empty;
- `ErrorResponse`: `ErrorCode code` (`UNSPECIFIED`, `NOT_ACCEPTED`, `RETRY_LATER`).

Domain:
- `LastBootStatus` (`Unknown`, `Accepted`, `Rejected`) on `ChargingStation`;
- `RecordAcceptedBoot` (firmware, `LastBootAt`, `LastHeartbeat`, status), `RecordRejectedBoot` (status only).

`StationLinkService` in Gateway:
- `streamBootStatus` (`RegistrationStatus?`) lives for the whole stream; admission rule: a boot in this stream → its result, otherwise `LastBootStatus` in the database;
- station not admitted → Warning + `ErrorResponse NOT_ACCEPTED`, the stream stays open;
- `RecordHeartbeatAsync` / `RecordConnectorStatusAsync`: short-lived `DbContext`, admission check, write;
- `ToDomain`: `ConnectorSentStatus` → `ConnectorStatus` via `switch`; unknown value → `Unknown` + Warning;
- unknown connector → Warning, nothing written, reply "received";
- database down: boot → `Pending`; heartbeat → current time (if admitted in this stream) or `NOT_ACCEPTED`; status → `RETRY_LATER` (if admitted in this stream) or `NOT_ACCEPTED`.

Migrations: `InitialCreate`, `AddStationBootInfo`, `AddLastBootStatus`.
Seeder: `ST-001` (Plus, 123), `ST-002` (Pro, 456); each has connectors 1 (Type2) and 2 (CCS).

Manual check with grpcurl:
grpcurl -plaintext -import-path src/VoltGrid.Contracts/Protos -proto station_link.proto -H 'station-id: ST-001' -d @ localhost:5205 voltgrid.station.v1.StationLink/Connect
{"messageId": "1", "bootNotification": {"model": "Plus", "serialNumber": "123", "firmwareVersion": "1.0.4"}}
{"messageId": "2", "heartbeat": {}}
{"messageId": "3", "statusNotification": {"connectorNumber": 2, "connectorStatus": "CONNECTOR_SENT_STATUS_OCCUPIED"}}

Next: stage 3.2, step 4 (exponential backoff with jitter: growing delay, a cap, reset after a successful connection), then step 5:
- `message_id` strategy (per-process counter vs `Guid.CreateVersion7()`);
- acceptance scenarios for the whole stage;
- decision 9 in `DECISIONS.md` (reconnect behaviour, the three state levels, which errors reconnect and which exit, handling of `NOT_ACCEPTED`).
  A separate read loop in the simulator is needed once Gateway starts sending commands itself (stage 9.3) or once the station has two independent message flows (stage 4).

## Technical debt
- Central Package Management (`Directory.Packages.props`): one version of each package for the whole solution; fixes the EF and gRPC version drift
- NU1903: vulnerable `Microsoft.OpenApi` 2.0.0 in Charging.Api (transitive dependency of the webapi template)
- `Program.cs` in Charging.Api is still the template, not cleaned up (Gateway's is already clean)
- Partial unique index "one active session per connector": revisit at stage 5.5 / 9.4
- Delete the template `Class1.cs` from `VoltGrid.Application`
- `git push` from the terminal via GitHub CLI (`gh auth login`); `bogdannalapko` has no sudo, so `brew install gh` from the admin account
- Contract: an "unknown command" reply (like OCPP `NotImplemented`); add together with the first command sent by Gateway
- README: deliberate limitation, stations are not authenticated (identity is taken from the connection, unverified)
- Align gRPC package versions (Gateway `Grpc.AspNetCore` 2.64.0 vs Contracts 2.84.0); consider `Grpc.AspNetCore.Server` instead of the metapackage
- Consider `WarningsAsErrors` for CA2017 in Gateway, so that a log placeholder mismatch breaks the build, not the stream
- Two concurrent streams with the same station-id: Gateway accepts both; decide at 9.1 (registry: the new stream evicts the old one)
- One database failure produces three `fail` entries (EF `Database.Connection`, EF `Query` and ours); tune EF category levels at stage 14
- The heartbeat interval is a constant in code; move it to `appsettings.json` once it needs changing without a rebuild
- The `NpgsqlException` filter in three `catch` blocks also catches schema errors (`PostgresException`, e.g. a forgotten migration), not only an unavailable database; narrow it to `IsTransient`
- The "database unavailable" filter is repeated in three `case` blocks; extract it into one method when a fourth message appears (stage 4)
- Connector 0 (OCPP: status of the whole station) is not supported and is currently logged as an unknown connector
- Contract: an error code for invalid data from a station (like OCPP `FormatViolation`)
- `RecordConnectorStatusAsync` loads the station even when admission is known from the stream (extra `SELECT`)
- A client disconnect produces two entries: `Error reading message` with a stack trace from `Grpc.AspNetCore.Server`, and our Warning; tune that category's level at stage 14
- Gateway: a client disconnect sometimes arrives as `OperationCanceledException` (`context.CancellationToken` cancelled) instead of `IOException`, escapes `Connect` and is logged as `fail` with a stack trace instead of a Warning (decision 6); handle together with stage 10.5
- Simulator: the log line "Reconnected without boot" is printed before the connection exists (`Connect` is lazy); reword it to "Already accepted, skipping boot"
- Simulator: after a reconnect every connector is reported as `Available` (a constant); real per-connector state is needed from stage 4