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