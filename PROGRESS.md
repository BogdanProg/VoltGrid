# PROGRESS

## Current stage
Stage 4.1 — `Authorize`, RFID tag check (new chat).
Session logic of stage 4 (authorization, sessions, tariffs) goes to `VoltGrid.Application`, not into the Gateway handlers: a handler translates the message and calls it.

## Done
- [x] Stage 0.1 — solution skeleton, 7 projects, git
- [x] Stage 0.2 — docker-compose: postgres, redis, kafka
- [x] Stage 1.1 — domain
- [x] Stage 1.2 — `DbContext`, first migration, seeding
- [x] Stage 2.1 — `.proto` contract, code generation, empty gRPC service
- [x] Stage 2.2 — bidirectional stream, `BootNotification`, admission policy, boot persisted to the database
- [x] Stage 2.3 — `Heartbeat`, `StatusNotification`, message admission, behaviour on database outage
- [x] Stage 3.1 — simulator: one station, `SendAsync` with timeout, boot retries, connector statuses
- [x] Stage 3.2 — simulator: heartbeat loop, reconnect with backoff and jitter, reconnect without boot, boot again after `NOT_ACCEPTED` (decision 9)
- [x] Stage 4.0 — refactoring before stage 4, no change in behaviour: the simulator station is a class (`SimulatedStation`); Gateway keeps only the transport in `StationLinkService`, one handler per message, per-stream state in `StreamState`

## Last working state
Stage 4.0 is closed: two commits without a change in behaviour (step 1 simulator, step 2 Gateway); the 3.1 and 3.2 scenarios were re-run by hand after each. The only visible changes are in the logs: the simulator prints the station id, Gateway logs under the handler categories and writes one "refused" Warning for every error reply.

`Station.Simulator`:
- `Program.cs` only wires things up: `StationOptions` for ST-001, one `GrpcChannel` and one client per process, `new SimulatedStation(options, client)`, `await station.RunAsync()`;
- `StationOptions` (record): `StationId`, `Model`, `SerialNumber`, `FirmwareVersion`, `ConnectorNumbers` (`IReadOnlyList<int>`); the values must match `DevDataSeeder` (ST-001, Plus, 123), otherwise boot is `Rejected`;
- `SimulatedStation` = one station: protocol timings as `private const` (`ReplyTimeoutSeconds` = 35, `MinBootRetrySeconds` = 25, `MinHeartbeatIntervalSeconds` = 5, `StatusRetrySeconds` = 15, `ReconnectBaseDelaySeconds` = 1, `ReconnectMaxDelaySeconds` = 60); `_headers` (`station-id`) built once in the constructor;
- state at three levels:
  - station (fields, survive reconnects): `_lastMessageId`, `_accepted` (the last boot of this station ended with `Accepted`), `_heartbeatIntervalSeconds` (saved at `Accepted`), `_reconnectAttempt` (failed attempts in a row);
  - call: `bootedInThisCall`, a local in `RunAsync`, reset after every `Connect`;
  - message: everything inside `SendAsync`;
- one object = one station = one `RunAsync` call; a station does everything in one `await` chain, so its fields need no locks (this changes once a station has two flows at stage 4);
- `RunAsync` = the former top-level loop; returns only on `Unauthenticated`:
  - outer loop = one call: `Connect` → inner loop; on a reconnectable failure log one line, wait `ReconnectDelay(++_reconnectAttempt)`, open a new call;
  - `ReconnectDelay(attempt)` (`static`, no station state): 1, 2, 4 … s, capped at 60 s, with equal jitter (half fixed, half random), so the shortest delay is 0.5 s; `_reconnectAttempt` resets to 0 only once the station fully works again (boot or skipped boot done, statuses accepted), not after `Connect` (lazy) and not after any reply;
  - inner loop = one admission attempt in the same call: not `_accepted` → `BootAsync`; statuses of all connectors (after a boot and after a reconnect without boot, log "Already accepted, skipping boot"); `HeartbeatLoopAsync`;
  - `NOT_ACCEPTED` on a status or a heartbeat → `StationNotAcceptedException`; caught only `when (!bootedInThisCall)` → `_accepted = false` → boot again in the same stream (`Pending` retries happen inside `BootAsync`); after a boot in the same stream it is a protocol violation and crashes the simulator (decision 8);
  - outer loop errors: `IOException` (including `HttpIOException`) and `RpcException` `Unavailable` / `Cancelled` / `Unknown` → reconnect; `Unauthenticated` → log and return; anything else (e.g. `InvalidOperationException`) → crash with a stack trace;
- `HeartbeatLoopAsync(call, intervalSeconds)`: `Task.Delay` before each heartbeat (the first one comes an interval after boot, since boot already records `LastHeartbeat`); `HeartbeatResponse` → log the server time; never returns normally;
- `SendAsync`, `BootAsync`, `ReportStatusAsync` work as in 3.1; `ReportStatusAsync` also throws `StationNotAcceptedException` on `NOT_ACCEPTED`;
- `StationNotAcceptedException` (own file): a separate type, so that `catch` can tell "boot again" apart from "connection lost" and "protocol violation";
- `Log(message)`: `HH:mm:ss {StationId} : message`, UTC;
- `message_id`: per-station counter, a new id for every attempt; a correlation id within one stream only, never an idempotency key (decision 9).

Gateway:
- `Program.cs`: `AddGrpc`; `AddDbContextFactory` (the factory is a singleton by default); the three handlers registered with `AddSingleton` (stateless: only a logger and the factory, both singletons; per-stream state travels in `StreamState`); `MapGrpcService<StationLinkService>`;
- `StationLinkService` = transport only, a new instance for every `Connect`: `station-id` header missing → Warning + `Unauthenticated`; `new StreamState(stationId)`; read loop; `switch` on `PayloadCase` picks the handler; unsupported payload → Warning, no reply; a reply with `Error` → one Warning `Message {MessageId} from {StationId} refused: {Error}, stream boot status {StreamBootStatus}`; sets `message_id` on the reply (handlers never see it) and writes it; `IOException` → Warning; `finally` → cleanup log;
- `StreamState`: `StationId`, `BootStatus` (`RegistrationStatus?`: result of the last boot in this stream, `null` = no boot yet), `AcceptedInStream`, `RefusedInStream` (`Rejected` or `Pending`); passed to every handler by reference, so the boot handler's change is seen by the next message; admission rule: a boot in this stream → its result, otherwise `LastBootStatus` in the database;
- `DatabaseErrors.IsUnavailable(ex)` (static): the one place that decides "database unavailable" (`NpgsqlException` itself or as `InnerException`);
- handlers in `Handlers/`: each returns the reply without `message_id`, logs under its own category, uses a short-lived `DbContext` from the factory:
  - `BootNotificationHandler`: `RegisterBootAsync` (station not found or serial mismatch → `Rejected`, model mismatch → Warning only, otherwise `Accepted` with firmware, `LastBootAt`, `LastHeartbeat`); database down → `Pending`; sets `state.BootStatus`; heartbeat interval 30 s (constant);
  - `HeartbeatHandler`: refused in this stream → `NOT_ACCEPTED`; otherwise `RecordHeartbeatAsync`; database down → current time if accepted in this stream (the heartbeat is just not recorded), otherwise `NOT_ACCEPTED`;
  - `StatusNotificationHandler`: `ToDomain` (`ConnectorSentStatus` → `ConnectorStatus`, unknown value → `Unknown` + Warning); refused in this stream → `NOT_ACCEPTED`; `RecordConnectorStatusAsync` (unknown connector → Warning, nothing written, reply "received"); database down → `RETRY_LATER` if accepted in this stream, otherwise `NOT_ACCEPTED`;
- a new message = a new handler in `Handlers/`, one `AddSingleton` line in `Program.cs` and one line in the `switch`.

Verified in 4.0:
- step 1 (simulator): normal start (every line prefixed with `ST-001`); `kill -9` on Gateway → `HttpIOException` at the next heartbeat, delays within their ranges (0.7, 1.6, 3.7, 4.9, 15.1, 17.1 s), return without boot, the next failure starts again from attempt 1; scenario B (Gateway restart with Postgres stopped) → `NOT_ACCEPTED` → boot in the same stream → `Pending` → `Accepted`, no new `Connecting`; simulator restart → boot; empty `station-id` → `Unauthenticated`, the process exits;
- step 2 (Gateway): normal start with the new log categories; Postgres stopped while the station is accepted in the stream → the station keeps getting `Heartbeat accepted`, Gateway logs `Heartbeat of ST-001 not recorded: database unavailable`; scenario A (`kill -9`, database up) → statuses accepted with no boot, admission from `LastBootStatus`; scenario B → `Status ... not recorded`, `refused: NotAccepted, stream boot status (null)`, boot `Pending` ×6, then `Accepted`; serial number `999` in the database → `Rejected` every 30 s, restored to `123` → the next boot is `Accepted` without restarting the station.

Findings from 4.0:
- The first failure right after `docker compose stop postgres` is `PostgresException` 57P01 (`terminating connection due to administrator command`), the following ones are `NpgsqlException` (`Connection refused`). EF wraps both in `InvalidOperationException` "likely due to a transient failure", i.e. Npgsql marks both as transient. Input for narrowing `DatabaseErrors.IsUnavailable` to `IsTransient`: 57P01 must stay "unavailable".
- The station is not told that a heartbeat was not recorded (by design since stage 2.3): a heartbeat is periodic and the next one carries the same information, and the station could not fix Gateway's database anyway. A status is an event that must not be lost, so it gets `RETRY_LATER`.
- A repeated `Rejected` boot sends no `UPDATE`: `LastBootStatus` is already `Rejected`, so EF change tracking finds nothing to write.

Verified in 3.2:
- heartbeat every 30 s; `LastHeartbeat` updates in the database;
- `kill -9` on Gateway → the station notices at its next heartbeat (`HttpIOException` on `WriteAsync`), retries every 5 s with `Unavailable` (`Error connecting to subchannel`), and comes back by itself once Gateway is up;
- Ctrl+C on Gateway → the same, but with `IOException` (`Connection reset by peer`);
- reconnect without boot → statuses and heartbeat, no `BootNotification` in Gateway, `LastBootAt` unchanged;
- Gateway restart with Postgres stopped → status gets `NOT_ACCEPTED` → boot in the same stream → `Pending` every 30 s → Postgres started → `Accepted` → statuses → heartbeat, with no new `Connecting`; `LastBootAt` updated;
- simulator restart → boot (new process, `accepted = false`);
- `station-id` header missing → `Unauthenticated`, the simulator exits;
- backoff: with Gateway down the delays grow (≈0.5–1, 1–2, 2–4 … s) up to 60 s; once the station works again, the next failure starts from attempt 1.

Findings from the 3.2 experiments:
- A connection that dies mid-stream arrives as a raw `IOException` / `HttpIOException` from the HTTP/2 layer, not as `RpcException`; only a failed connect gives `RpcException` `Unavailable`.
- The station learns that Gateway is gone only when it next touches the stream, i.e. at the next heartbeat (up to 30 s later).
- `client.Connect` is lazy: the HTTP/2 request leaves with the first `WriteAsync`. That is why `Unauthenticated` arrives on `MoveNext`, and why a log line right after `Connect` cannot claim the station is connected.
- Ctrl+C on Gateway waits for `HostOptions.ShutdownTimeout` (30 s), because `Connect` never completes on its own; streams keep working meanwhile and are then aborted all at once. Input for stage 10.5 (graceful shutdown, draining streams).
- `kill -9` skips the `finally` in `Connect`, so no cleanup runs. Once the Redis registry exists, its entry would stay behind. Input for stage 9.5 (TTL, lease).
- `message_id` gaps: the counter increments before `WriteAsync`, so failed sends consume ids (Gateway saw 5 → 13 after a restart); after a simulator restart the ids start again from 1. Decided in decision 9: `message_id` is a correlation id within a stream, not an idempotency key.
- With a fixed 5 s delay every station comes back within the same 5 s after Gateway recovers (thundering herd). Fixed in step 4 with backoff and jitter.

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

Migrations: `InitialCreate`, `AddStationBootInfo`, `AddLastBootStatus`.
Seeder: `ST-001` (Plus, 123), `ST-002` (Pro, 456); each has connectors 1 (Type2) and 2 (CCS).

Manual check with grpcurl:
grpcurl -plaintext -import-path src/VoltGrid.Contracts/Protos -proto station_link.proto -H 'station-id: ST-001' -d @ localhost:5205 voltgrid.station.v1.StationLink/Connect
{"messageId": "1", "bootNotification": {"model": "Plus", "serialNumber": "123", "firmwareVersion": "1.0.4"}}
{"messageId": "2", "heartbeat": {}}
{"messageId": "3", "statusNotification": {"connectorNumber": 2, "connectorStatus": "CONNECTOR_SENT_STATUS_OCCUPIED"}}

Next: stage 4.1 in a new chat.
A separate read loop in the simulator is needed once the station has two independent message flows (stage 4: meter values during a session next to heartbeats) or once Gateway starts sending commands itself (stage 9.3).

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
- `DatabaseErrors.IsUnavailable` also catches schema errors (`PostgresException`, e.g. a forgotten migration), not only an unavailable database; narrow it to `IsTransient` (one place now); 57P01 (admin shutdown) must stay "unavailable"
- Connector 0 (OCPP: status of the whole station) is not supported and is currently logged as an unknown connector
- Contract: an error code for invalid data from a station (like OCPP `FormatViolation`); an unsupported payload currently gets no reply at all, so the station waits 35 s and reconnects
- `StatusNotificationHandler.RecordConnectorStatusAsync` loads the station even when admission is known from the stream (extra `SELECT`)
- A client disconnect produces two entries: `Error reading message` with a stack trace from `Grpc.AspNetCore.Server`, and our Warning; tune that category's level at stage 14
- Gateway: a client disconnect sometimes arrives as `OperationCanceledException` (`context.CancellationToken` cancelled) instead of `IOException`, escapes `Connect` and is logged as `fail` with a stack trace instead of a Warning (decision 6); handle together with stage 10.5
- Simulator: after a reconnect every connector is reported as `Available` (a constant); real per-connector state is needed from stage 4
- Boot retry after `Pending` (30 s) and status retry after `RETRY_LATER` (15 s) are still fixed; add jitter at stage 10.3
- Logic placement (to revisit at stage 9): boot admission and heartbeat stay in Gateway (synchronous reply, no extra hop to `Charging.Api`); sessions, authorization and tariffs go to `Charging.Api`; statuses move to Kafka `station-events` at stage 7
- Watchdog (stage 8.4): while the database is down `LastHeartbeat` is not updated; after an outage longer than the offline threshold a station may look offline until its next heartbeat (up to one interval)
- Unit tests for the authorization rule (Blocked over Expired, expiry boundary, tag normalisation): stage 16
- Time in Application comes from DateTimeOffset.UtcNow; switch to TimeProvider when the expiry boundary gets tests: stage 16