# BIO-TRN-002 — Implement boarding/alighting and bus-presence events

**Wave:** W11 / W11.2 · conflict group `transport-core`
**Owner:** Tunde — Identity & Tenant Engineer
**Dependencies:** BIO-TRN-001 (routes/vehicles/stops/person-assignment model), BIO-ATT-001 (attendance event core — the direct structural template for this story)

## User story

As transport operations, I need boarding and alighting events using configurable UHF/NFC/face combinations.

## AC → evidence map

| AC | What it asks | Where it's proven |
|---|---|---|
| AC01 | Board/alight events include route, vehicle, stop/location and identity evidence | `Domain/TransportEvent.cs` (immutable capture record — mirrors BIO-ATT-001's own `AttendanceEvent`), `Domain/TransportEvidenceEntry.cs` (a **list** of evidence entries, not one — see "Evidence is a list" below), `Domain/ITransportRegistryGateway.cs`'s cross-story existence/active-status checks, `tests/features/bio-trn-002/TransportEventServiceTests.cs` |
| AC02 | Duplicate reads are debounced/idempotent | `TransportEvent.ComputeFingerprint`/`IdempotencyKey`, `TransportEventLockRegistry`, identical mechanism to BIO-ATT-001's own AC03; a genuine 20-way concurrent-capture race test, `TransportEventsHostChannelTests.cs`'s real-host proof |
| AC03 | Missing alight/exception states are handled | `Domain/TransportPresenceEvaluator.cs` (pure, read-time-only function — never mutates any stored `TransportEvent`), `tests/features/bio-trn-002/TransportPresenceEvaluatorTests.cs` (including the identical-events-two-different-read-times proof) |
| AC04 | Events can trigger configured notifications | `Domain/TransportNotificationConfig.cs` (feature-local, CAS-versioned) + `Domain/TriggeredNotification.cs` (real, readable-back log) — see "AC04: a feature-local notification trigger, not a BIO-RPT-001 integration" below |
| AC05 | `/transport` connected through `TransportService` | `Demo/TransportEventDemoBridgeService.cs` now answers `/api/v1/transport/primary-action`, taking over from BIO-TRN-001's own bridge — see "Why this story edits BIO-TRN-001's own endpoint mapping" below |

## Design decisions and scope boundaries

### Evidence is a LIST, not a single value — a genuine structural difference from BIO-ATT-001

AC01 says "identity evidence," but the user story itself says "configurable UHF/NFC/face
**combinations**" (plural, combined) — a real capture may pair a UHF tag read with a face match
for higher confidence, and both entries belong on the same event. `TransportEvent.Evidence` is
therefore `IReadOnlyList<TransportEvidenceEntry>` (at least one required), not BIO-ATT-001's own
single-`VerificationEvidence` shape. This is a deliberate, story-justified divergence from the
otherwise-identical template, not gratuitous complexity — everything else about capture/AC02's
idempotency mechanism is copied field-for-field from `AttendanceEventService.CaptureAsync`.

### AC03: missing-alight exception, computed at read time only

`TransportPresenceEvaluator.Evaluate(personId, date, dayEvents, now)` is a pure function — same
(events, now) in, same `DerivedTransportPresence` out, every time. An unmatched `Board` (no later
`Alight` in the same day's events) classifies as `OnBoard` while still plausibly in progress
(within `MissingAlightExceptionWindowHours` = 12 hours of boarding, same calendar day) and
`MissingAlightException` once that window has passed, or unconditionally for any date strictly
before today. The same stored events classify differently depending on when they are read —
proven directly by `TransportPresenceEvaluatorTests.AC03_g_the_IDENTICAL_stored_events_classify_differently_at_two_different_read_times_...`
— the same "refuse auto-flip, compute in effect at read time" discipline BIO-IDN-002/BIO-ATT-001/
BIO-TRN-001 already established, applied fresh here for the identical reason (no background
scheduler infrastructure exists to keep a stored derived value from going stale).

### AC04: a feature-local notification trigger, not a BIO-RPT-001 integration

BIO-RPT-001 (notification event/rule foundation) is **not a declared dependency** of this story
(only BIO-TRN-001 and BIO-ATT-001 are). Its `IDomainEventBus`/`DomainEventEnvelope` mechanism is
also hard-constrained by the real, frozen `contracts/schemas/event-envelope.schema.json`
(Kemi-owned) to exactly five `aggregate_type` values (`tenant`/`person`/`credential`/`device`/
`document_session`) — there is no sixth value for a transport/attendance event, and adding one
needs Kemi's approval, which is outside this story's authority to request unilaterally.

So AC04 is satisfied with a real, testable, **feature-local** mechanism instead:
`TransportNotificationConfig` (mirrors `AttendancePolicy`/`VisitorTenantConfig`'s own CAS-versioned
shape — `NotifyOnMissingAlightException`, off by default) and `TriggeredNotification` (a real,
append-only, readable-back record). `TransportEventService.CaptureAsync` checks, after committing
a capture, whether the day it belongs to now evaluates to `MissingAlightException` and, if the
tenant's config enables it, appends a `TriggeredNotification` (deduplicated per
`(tenant, person, event, rule)` so a re-capture of the same idempotency key never double-fires).
This is real, deliberate, precedented design — the same "caller-supplied evaluation inputs vs. a
live gateway" disclosure style BIO-ACC-001 used for its own undeclared-dependency boundary — not a
shortcut. A future story that declares BIO-RPT-001 as a real dependency would translate this same
real `TriggeredNotification` record into an actual delivered notification.

**The trigger check runs only from `CaptureAsync` (the one genuine write path), never from
`GetPresenceStatusAsync` (the read-only status endpoint)** — a GET must stay side-effect-free. A
caller proves this end to end by capturing a `Board` event with an `occurred_at` already outside
the exception window (a legitimate late/backfilled capture — `TransportEvent.Capture` places no
"must be close to now" constraint on `occurred_at`, so this is not a clock trick) — the exception
is genuine at the moment of capture, so the trigger fires immediately, no second call needed.

### Why this story edits BIO-TRN-001's own endpoint mapping

`web/features/transport/live.ts`'s `transportLivePort` POSTs to `/api/v1/transport/primary-action`
— BIO-TRN-001's own bridge (`TransportDemoBridgeService.ViewDemoRegistrySummaryAsync`) answered it
with a registry summary, explicitly disclosed at the time as standing in for a boarding/alighting
event log that did not exist yet in that story's own scope (AC01–04 there are the Route/Vehicle/
Stop/PersonAssignment structural model only). This story now provides that exact capability, so it
takes over the route: `TransportEndpoints.cs`'s `/primary-action` mapping and its
`PrimaryActionAsync` handler were **removed** from `api/features/bio-trn-001/Api/TransportEndpoints.cs`
(not left as dead/duplicate code — two endpoint groups mapping the same route is a genuine
ambiguity, not a harmless overlap), and `ViewDemoRegistrySummaryAsync` was removed from
`api/features/bio-trn-001/Demo/TransportDemoBridgeService.cs` for the same reason. `assign-route`
is completely untouched — still answered by BIO-TRN-001's own bridge.

This is a narrow, disclosed, pre-anticipated edit to an already-accepted sibling story's files —
BIO-TRN-001's own README already stated the placeholder was standing in for exactly this
capability, so the handoff was not a surprise the moment it landed, and I own both stories. Every
edit is confined to the doc comment + the two lines removing the route mapping/handler; nothing
else in BIO-TRN-001's own domain logic, tests, or other endpoints changed (confirmed: its own
26/26 feature tests and its own `TransportHostChannelTests.cs` still pass unmodified, including the
one existing test that exercises `/primary-action` through the real host — its assertion only
checks the response contains `"Transport:"`, which this story's own message still does).

### A genuine cross-story WRITE, not merely a read — and the captive-dependency bug it surfaced

`Demo/TransportEventDemoBridgeService.cs` needs a real, active route/stop/vehicle to capture demo
events against, but cannot assume BIO-TRN-001's own demo bridge (a separate Singleton, possibly
never yet called) has already seeded one — a user could click "View boarding/alighting log" before
ever clicking "Assign route." `ITransportDemoSeedProvisioner.EnsureDemoSeedAsync` (implemented in
`BioTrn002Module.TransportDemoSeedProvisioner`) finds the shared demo route by its fixed name
(`"Route A — Riverside Loop"`, matching BIO-TRN-001's own bridge constant exactly) or creates it
through BIO-TRN-001's own aggregate factories (`RouteRecord.Create`/`StopRecord.Create`/
`VehicleRecord.Create`, reachable only via this assembly's `InternalsVisibleTo` grant) — idempotent,
safe to call from either bridge, in either order.

**A real DI captive-dependency bug was caught and fixed during this story's own build, not
shipped.** The first draft registered a single Singleton adapter implementing both the ambient-
tenant `ITransportRegistryGateway` (needs the Scoped `ITenantContextAccessor`) and the demo-seed
provisioning method — a Singleton holding a Scoped dependency, the exact captive-dependency shape
this codebase's own BIO-IDN-001 lesson already closed once (a Singleton pins the first request's
scope forever). Caught at build/design time by re-deriving each interface's own actual lifetime
need rather than assuming one adapter class should do both: `ITransportRegistryGateway`
(`TransportRegistryGatewayAdapter`, Scoped, depends on `ITenantContextAccessor`) and
`ITransportDemoSeedProvisioner` (`TransportDemoSeedProvisioner`, Singleton, depends only on the
Singleton repositories, takes `tenantId` explicitly — never resolves it ambiently) are two
genuinely separate classes/interfaces/registrations, not one adapter split awkwardly.

## Deliberately out of scope for this story

- Real delivery through BIO-RPT-001's notification channels (email/SMS/webhook/push) — see "AC04"
  above. `TriggeredNotification` is the real, disclosed handoff point for a future story that
  declares BIO-RPT-001 as an actual dependency.
- Reconciling boarding/alighting activity across MULTIPLE simultaneous route/vehicle pairs in one
  day for the same person — `TransportPresenceEvaluator` tracks the single most recent open board
  window across the day's events, not a per-route/vehicle ledger; no AC asks for the latter, and a
  person genuinely boarding two different vehicles without an intervening alight is itself an
  unusual, exception-worthy scenario already captured by the evaluator's own logic.
- A real device/reader offline sync client — same disclosed boundary BIO-CRD-006/BIO-ATT-001
  already carry; this story provides the backend capture/evaluation building blocks a future
  offline reader would call.
