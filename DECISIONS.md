# DECISIONS

Key architectural decisions with reasoning. Format: context → decision → cost.

---

## 1. Station identifier is a string, not a Guid

**Date:** 2026-09-09 (stage 1.1)

**Context.** A station identifies itself in `BootNotification` with a string code
(`STATION-042`) printed on the enclosure and burned into the device config.
The same code addresses it everywhere else: the Redis registry key
(`station:{id}`) and the Kafka partition key.

**Decision.** `ChargingStation.Id` is a `string` — a natural key.

**Reasoning.** A surrogate Guid would have to be looked up by that string on
every inbound message, hundreds of times per second, purely to translate one
identifier into another. Surrogate keys pay off when there is no natural key or
when the natural one changes. Neither applies here.

**Cost.** If a station is physically replaced but keeps the same code on the
enclosure, the histories of two different devices merge. Standard operator
practice is to retire the code together with the hardware and never reuse it.

---

## 2. Connector identity is the composite key (StationId, Number)

**Date:** 2026-09-09 (stage 1.1)

**Context.** OCPP numbers connectors within a station: 1, 2, 3. The number alone
addresses nothing — connector 1 exists on every station in the network.

**Decision.** A connector is its own aggregate, keyed by `(StationId, Number)`.
`ChargingStation` has no `Connectors` navigation property.

**Reasoning.** The contended resource is the socket, not the enclosure. If a
connector could only be modified through its station, two concurrent updates to
different connectors of the same station would produce a false conflict, and the
station row would become a write hotspot as status notifications pile up.
The navigation property is left out deliberately: keeping it would make the
wrong access path the shortest one. The distributed lock in stage 9.4 will key
on `connector:{stationId}:{number}`.

**Cost.** Two invariants no longer hold in memory and move to the database:
"this connector belongs to an existing station" (foreign key) and "connector
numbers are unique within a station" (unique index).

---

## 3. Price is captured at session start

**Date:** 2026-09-09 (stage 1.1)

**Context.** An operator changes a tariff. A session starts at 23:50 at 12/kWh,
the price becomes 15 at 00:00, and the session ends at 00:30. A month later the
customer disputes the invoice.

**Decision.** `ChargingSession` stores `decimal PricePerKwh`, a copy of the price
at the moment the session started. `TariffId` is kept for reporting only.
The copy, not the reference, is the source of truth for money.

**Reasoning.** A reference returns the current state; a copy returns the state at
the time of the event. Reading the price through `TariffId` would recompute a
historical invoice every time it is opened, so the same session could show
different totals on different days. Receipts and invoices work the same way.

**Cost.** Time-of-use tariffs are not supported: a customer pays the start-time
price even for kWh consumed after the change. Splitting consumption across
pricing periods is a separate billing problem, deliberately out of scope.
---

## 4. Station identity comes from call metadata, not from BootNotification

**Date:** 2026-10-20 (stage 2.2)

**Context.** Gateway must know which station is on the other end of a stream.
Two options: a `station-id` header sent when the stream is opened, or a
`station_id` field inside `BootNotification`. A station sends BootNotification
only after it boots, not after every reconnect: when LTE drops for 10 seconds,
the station opens a new stream and goes straight to Heartbeat.

**Decision.** Identity is a property of the connection. The station sends
`station-id` in gRPC call metadata; Gateway reads it from
`context.RequestHeaders` at the start of `Connect`, before the first message.
A stream without the header is rejected with `Unauthenticated`. Messages carry
no `station_id` field.

**Reasoning.** Identity arrives with every connection (every reconnect), not
with every boot (rare). With BootNotification-based identity, a reconnected
stream would stay anonymous for hours. A field that is not in the message
cannot contradict the stream it arrived on. OCPP-J does the same: the charge
point identity is part of the WebSocket URL.

**Cost.** The header is only a claim: Gateway does not verify that the station
really is ST-001 (no authentication, a documented limitation). Every client,
including test tools like grpcurl, must send the header.
---
## 5. Boot admission: the database decides, the device reports hardware facts

**Date:** 2026-10-20 (stage 2.2)

**Context.** BootNotification carries the station's model and serial number,
read from its own hardware. The database holds what the operator entered at
installation. They can disagree: a typo, a replaced controller board, or a
different device using an existing station code (stations are not
authenticated).

**Decision.**
- Station code not in the database → `Rejected`.
- Serial number differs from the database → `Rejected`, Warning with both values.
- Model differs → `Accepted`, Warning for the operator.

**Reasoning.** Fail closed where money is at stake. A different device under an
existing code would mix two meter histories into one station, and consumption
(and therefore the bill) is computed from those readings. An unavailable
station is visible and fixable; a wrong invoice is found by the customer a
month later. A model mismatch does not affect metering, so blocking a working
station over a data-entry typo is not worth the lost sessions.

**Cost.** After a legitimate hardware replacement the station stays rejected
until the operator updates the serial number, so the replacement procedure must
include that update. Rejected stations retry boot after the interval, so they
recover on their own once the data is fixed.
---

## 6. Message handling failures: expected ones keep the stream, unexpected ones end it

**Date:** 2026-10-27 (stage 2.2)

**Context.** A station's stream lives for days, and every message is handled
inside it. An exception that escapes `Connect` ends the stream: the station
sees `Unknown`, reconnects and resends. Some failures are expected and are not
the station's fault (the database is down, the station's network drops);
others are bugs.

**Decision.**
- Station disconnects (`IOException` from the request stream): Warning, one
  line, no stack trace.
- Database unavailable while handling BootNotification (`NpgsqlException`,
  directly or as the inner exception): Error with the exception, reply
  `Pending`, keep the stream.
- Anything else is not caught: the stream ends and the gRPC framework logs
  `fail`.

**Reasoning.** The log level follows whose problem it is: a dropped LTE link is
routine, a missing database needs someone to act. `Pending` tells the station
it is not at fault and should retry boot after the interval, without a
reconnect storm from a thousand stations. `Rejected` would send the operator
looking for bad station data that is not there. Unexpected exceptions are bugs;
catching them would hide them behind `Pending` or a Warning, so they fail
loudly instead.

**Cost.** A bug in a message handler still drops the station's connection until
it is fixed. All stations told `Pending` retry after the same interval, so a
recovering database meets a burst of boots; jitter is planned for stage 10.3.

---

## 7. Message admission: the stream's boot decides, otherwise the database

**Date:** 2026-11-04 (stage 2.3)

**Context.** After boot a station sends Heartbeat and StatusNotification, and
from stage 4 meter values that bills are computed from. A station that was
never admitted (unknown code, serial mismatch, `Pending`) may still send them:
firmware has bugs and stations are not authenticated. A station that was
admitted reconnects after a network drop without booting again (decision 4),
so a new stream knows nothing about the earlier boot.

**Decision.**
- Admission rule: if this stream had a boot, its result decides; otherwise
  `ChargingStation.LastBootStatus` in the database decides.
- `LastBootStatus` (`Unknown`, `Accepted`, `Rejected`) is stored for every
  accepted and rejected boot. `Pending` is never stored. A rejected boot
  changes only the status: firmware, boot time and last heartbeat stay those
  of our own device.
- A message from a station that is not admitted gets an `ErrorResponse`
  `NOT_ACCEPTED` with the same `message_id`, plus a Warning. The stream stays
  open.
- Database unavailable:
  - Heartbeat, admitted in this stream: reply with the current time, do not
    record the heartbeat. Admission unknown: `NOT_ACCEPTED`.
  - StatusNotification, admitted in this stream: `RETRY_LATER`. Admission
    unknown: `NOT_ACCEPTED`.
- StatusNotification with `UNSPECIFIED` or an unknown status value: record
  `Unknown`, Warning with the raw value, reply as received. Unknown connector
  number: record nothing, never create a connector, Warning, reply as received.

**Reasoning.** Fail closed where money is at stake (decision 5): a different
device under an existing code must not update heartbeats, statuses or, later,
meter values. Closing the stream adds no protection (not processing the
message is the protection) and turns a database outage into a reconnect storm.
Heartbeat is periodic, so one unrecorded heartbeat is replaced 30 s later.
StatusNotification is an event sent once per change, so losing it leaves the
map wrong until the next change; the station must resend it. A status
notification means the old status is stale even if the new one is unreadable,
and `Unknown` hides the connector from drivers, which is safer than showing an
occupied connector as available. An error reply would not help a station that
cannot fix its own firmware; the operator learns from the log. Connectors are
never created from station data: a connector needs a tariff, which is the
operator's price, and stations are not authenticated.

**Cost.**
- One gap remains: a device that got `Pending` because the database was down
  can reconnect without booting, and the stale `Accepted` in the database
  admits it. This needs a database outage during that boot and firmware that
  skips boot.
- After a Gateway restart almost every stream has no boot, so during a
  database outage almost the whole network gets `NOT_ACCEPTED`, boots, gets
  `Pending`, and the recovering database meets a burst of boots (jitter is
  planned for stage 10.3).
- Correctness depends on station behaviour: boot after `NOT_ACCEPTED`, send
  the status of every connector after `Accepted`, resend after `RETRY_LATER`.
  The simulator implements this from stage 3.1.
- OCPP connector 0 (status of the whole station) is not supported and is
  logged as an unknown connector.