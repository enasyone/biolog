# BIO-TRN-001 — Implement routes, buses and assignment model

**Wave:** W11 / W11.1 · conflict group `transport-core`
**Owner:** Tunde — Identity & Tenant Engineer
**Dependencies:** BIO-IDN-001 (person lifecycle), BIO-DEV-001 (device registry)

## User story

As school transport administrators, we need route, vehicle, stop and person assignment models.

## AC → evidence map

| AC | What it asks | Where it's proven |
|---|---|---|
| AC01 | Route/vehicle/stop lifecycle is versioned | `Domain/{Route,Vehicle,Stop}.cs` (mutable, CAS-versioned aggregates), `Domain/TransportTransitions.cs` + each `InMemory*Repository.ReplaceAsync` (dual-boundary transition validation), `tests/features/bio-trn-001/RegistryServiceTests.cs` |
| AC02 | Person assignments support validity windows | `Domain/PersonAssignment.cs` (`ValidFrom`/`ValidUntil`), `PersonAssignmentService.CreateAsync`'s overlap invariant, `Domain/TransportStatusEvaluator.cs` (read-time effective status, never auto-mutated), `tests/features/bio-trn-001/{PersonAssignmentServiceTests.cs,TransportStatusEvaluatorTests.cs}` |
| AC03 | Vehicle/device linkage is supported | `Domain/Vehicle.cs`'s `DeviceId`, `Domain/IDeviceExistenceGateway.cs`, `TransportHostChannelTests.cs`'s real-host proof |
| AC04 | Tenant/campus boundaries apply | `CampusId` on Route/Vehicle/Stop, `PersonAssignmentService.CreateAsync`'s campus-consistency check |
| AC05 | `/transport` connected through `TransportService` | `Demo/TransportDemoBridgeService.cs`, `Api/TransportEndpoints.cs`'s `/primary-action`/`/assign-route` routes, `web/next.config.mjs` rewrite |

## Architecture — four independently versioned aggregates

Every aggregate is a mutable, CAS-versioned `sealed record` (internal constructor, `get`-only
`Id`/`TenantId` even across `with`, internal transition methods) — the same shape BIO-VIS-001's
`VisitorRecord` and BIO-ATT-001's `AttendanceEvent`-family aggregates establish for this codebase.
Every mutation is dual-boundary validated: the service checks legality before calling a mutation
method, and the in-memory repository's `ReplaceAsync` independently re-checks
`TransportTransitions` at the persistence boundary (BIO-IDN-001's own discipline).

- **`RouteRecord`** — `Draft → Active → Retired`, one-way. Carries an ordered `Stops` list
  (`RouteStopEntry{StopId, SequenceOrder, EstimatedOffsetMinutes?}`); each referenced stop is
  existence/active-checked against this feature's own `IStopRepository` at create/update time
  (in-process, no cross-project gateway needed — `Stop` lives in this same feature).
- **`VehicleRecord`** — `Active ⇄ OutOfService`, `Retired` terminal from either (a vehicle taken
  out of service for repairs legitimately returns to `Active`; this is the one aggregate here that
  is *not* strictly one-way, a deliberate, documented asymmetry from Route/Stop). Carries the
  AC03 `DeviceId` linkage.
- **`StopRecord`** — `Active → Retired`. Independently reusable across multiple routes; retiring a
  stop does not retroactively rewrite any route's own historical stop list.
- **`PersonAssignment`** — a person's assignment to a route/stop with a validity window. The only
  post-creation mutation is `End` (setting `ValidUntil` on an open-ended or future window) —
  editing a route/stop/person in place is not supported; ending one and creating a new one is the
  sanctioned path, so the audit trail is unambiguous about which window applied when.

## Why no canonical identifier-registry prefix

`contracts/schemas/identifier-registry.json` is Kemi-owned ("adding or changing an entry requires
an amending ADR and recorded Kemi approval") and has no `route`/`vehicle`/`stop`/`assignment`
entry. Rather than unilaterally requesting four new types, this feature follows BIO-ACC-001's
(`PolicyKey`) and BIO-VIS-001's (`VisitorRecord.Id`) own disclosed precedent for the identical
situation: every aggregate's `Id` is a plain, undecorated `Guid.CreateVersion7()` string, never a
registry-prefixed value.

## AC02 — the overlap invariant, and why the lock is acquired where it is

At most one assignment per `(TenantId, PersonId)` may have a validity window overlapping
another's. This is a tenant/person-scoped invariant across *multiple* records, not any single
record's own state — a per-record lock alone cannot protect it. `PersonAssignmentService.CreateAsync`
acquires `PersonAssignmentUniquenessLockRegistry` (keyed `(TenantId, PersonId)`) **first**, checks
the overlap under that lock via a half-open-interval comparison (`WindowsOverlap`, treating a
`null` `ValidUntil` as +infinity), then creates the record — the exact BIO-IDN-003 lesson ("lock
scope must match invariant scope") re-applied, not rediscovered. A genuine 20-way concurrent-create
race test (`PersonAssignmentServiceTests.AC02_h_...`) proves only one caller wins.

Overlap windows are half-open (`[from, until)`): an assignment ending exactly when the next one
begins is *not* a conflict (`AC02_g_a_non_overlapping_sequential_window_...`).

## AC03 — vehicle/device linkage is existence-checked, not status-gated

Re-derived, not pattern-matched from BIO-VIS-001's own gateway-boundary reasoning: pairing a
vehicle with a not-yet-`Active` device (`DeviceStatus.PendingActivation`) is normal pre-deployment
inventory work, not an authorization decision. `IDeviceExistenceGateway` checks existence only.
BIO-DEV-001 is not a story-keyed feature project — it lives inside `Biolog.Api` itself
(`api/Biolog.Api/Features/DeviceRegistry/**`) — so this gateway, implemented by
`BioTrn001Module.DeviceExistenceGatewayAdapter` wrapping `DeviceService.GetAsync` directly, is
this feature's *only* coupling to it, the same gateway-not-`ProjectReference` discipline every
cross-story dependency in this codebase uses.

## AC04 — tenant/campus boundaries apply

Every read/write is tenant-scoped as usual (`RequireTenant()`). `CampusId` on Route/Vehicle/Stop
is an **additional, finer, feature-local scoping field — not a registered entity**: no Campus
service/registry exists anywhere in this repository and none is a declared dependency of this
story, so `CampusId` is shape-validated only (non-empty when present), never existence-gated
against an external source — the same disclosed-boundary style as BIO-ATT-001's `device_id` and
BIO-VIS-001's watchlist result. The one real rule enforced: when creating a `PersonAssignment`, if
the target route *and* the target stop both declare a campus, they must match
(`TransportError.CampusMismatch`) — neither declaring one, or only one declaring one, is not a
mismatch (campus scoping is optional, not mandatory).

Automatic expiry (the assignment's own "is this currently in force" question) is computed at READ
time only (`TransportStatusEvaluator.EffectiveStatus`, a pure function returning a genuinely
distinct `EffectiveAssignmentStatus` enum the persisted record can never hold) — the same
"refuse auto-flip, compute in effect at read time" discipline BIO-IDN-002/BIO-ATT-001/BIO-VIS-001
established, reapplied fresh here for the identical reason (no background scheduler exists to keep
a stored derived value from going stale).

## Why registry management stays open, and assignment does not (ADR-0006 Decision 1/2)

Re-derived, not pattern-matched (BIO-ACC-001's own QA-01 lesson: a sibling story's precedent must
be re-justified against what THIS story's own data discloses, never assumed from endpoint shape
alone):

- **Route/Vehicle/Stop create/update/retire, and every read/list, stay open** to any asserted
  actor — audit-attribution only, no permission or provenance check. Registry/inventory content
  here (a route's name, a bus's plate number, a stop's location) is comparable in sensitivity to
  BIO-ATT-001's own raw presence events and PERSON's own name/DOB, both of which stay open across
  this codebase — not to BIO-ACC-001's access-policy rule content, which is gated everywhere
  because it is literally the blueprint for bypassing physical security. A bus route name is not.
- **`PersonAssignmentService.CreateAsync`/`EndAsync` are this feature's one authorization
  decision.** Deciding a specific person may board a specific vehicle on a specific route is
  granting physical transport access — the same class of decision as BIO-VIS-001's
  `visitor.approve` and BIO-ATT-001's policy-set. Gated by `transport.assign_manage` via
  `IPermissionGateway` **and** a verified ADR-0006 Decision 3 provenance proof
  (`ActorProvenanceGate`, hand-ported feature-locally exactly as every prior story's own).

Every mutation — gated or not — is still audited via the same audit-atomicity pattern
(ADR-0003 Decision 7: per-record lock, audit-before-commit, `ExistsAsync`-on-throw verification,
`CancellationToken.None` post-confirm). Auditability and authorization-gating are separate
concerns; AC02's "auditable" requirement covers every host-approval-equivalent transition, but the
same discipline is applied uniformly across all four aggregates for consistency.

## AC05 — `/transport` is connected, without redesign

`web/features/transport/{transport-page.tsx,ports.ts,mock.ts}` are completely untouched
(harness-enforced — `BIO-TRN-001.test.sh` greps for zero `BIO-TRN-001` mentions in those three
files). `live.ts` already existed as BIO-UX-005/010 scaffolding, POSTing headerless requests to two
fixed routes (`primary-action` — "View boarding/alighting log" — and `assign-route` — "Assign
route"). This story adds the real `Biolog.Api` endpoints those routes resolve to, backed by
`TransportDemoBridgeService` (Singleton, seeds one real demo `Route`/`Stop`/`Vehicle` per tenant
and exercises genuine domain preregister-style route/stop seeding plus real `PersonAssignment`
creation — not a canned string, the same AC05 pattern BIO-ATT-001's/BIO-VIS-001's own demo bridges
established).

**Boarding/alighting event capture does not exist in this story's scope.** AC01–04 are the
Route/Vehicle/Stop/PersonAssignment structural model only, not the event stream itself — that is
plausibly a sibling BIO-TRN-002/003 story's territory, a disclosed sibling-story boundary, not
something to build here. `route-manifest.ts`'s primary action ("View boarding/alighting log") is
therefore answered by `TransportDemoBridgeService.ViewDemoRegistrySummaryAsync` with a **real
registry/assignment summary** instead (active-assignment count, route/vehicle names) —
explicitly documented in the response message itself as *not* a boarding/alighting log, a disclosed
HYBRID substitution, the same honest-boundary style every prior story's own AC05 section uses.

## Deliberately out of scope

- Boarding/alighting event capture (see above — a sibling story's territory).
- A real Campus registry/entity — `CampusId` is feature-local scoping only (see AC04 above).
- Editing a `PersonAssignment`'s route/stop/person in place — end one, create a new one.
- Route-to-vehicle assignment (which bus serves which route on which day) — this story models the
  route/vehicle/stop/person registries independently; scheduling a specific vehicle onto a
  specific route's specific run is not asked for by any of this story's ACs.

## Verification

`dotnet build` clean · 26/26 feature unit tests (`Biolog.Features.Transport.Tests`, including a
genuine 20-way concurrent-overlap race) · 6/6 new `TransportHostChannelTests` (real BIO-DEV-001
gateway reachability, both authorization-gate rejection paths, both AC05 bridge routes) · full
`Biolog.IntegrationTests` suite green, no regressions · 34/34 `BIO-TRN-001.test.sh` structural
checks · `scripts/quality.sh --full` QUALITY PASS.
