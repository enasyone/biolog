# BIO-ATT-003 — Implement workforce attendance policies

**Owner:** Tunde — Identity & Tenant Engineer
**Wave:** W12
**Conflict group:** `attendance-policy`
**Dependencies:** BIO-ATT-001

## User story

As workforce tenants, I need clock-in/out, shift, lateness, overtime and absence interpretation.

## AC → evidence map

| AC | Statement | Where it lives |
| --- | --- | --- |
| AC01 | Shift schedules and grace periods are configurable. | `Domain/WorkforceShift.cs` (many named shifts per tenant, CAS-versioned), `WorkforceShiftService.cs` |
| AC02 | Clock pairs are validated. | `Domain/ClockPairEvaluator.cs` (pure function), `Domain/ClockPairStatus.cs` |
| AC03 | Overtime is derived, not raw-event mutation. | `Domain/OvertimeEvaluator.cs` (pure function over already-validated pairs) |
| AC04 | Exceptions require audit reason. | `Domain/WorkforceAttendanceAdjustment.cs` (append-only, `Reason` required and validated), `WorkforceAttendanceService.CreateAdjustmentAsync` |
| AC05 | `/attendance` connected through `AttendanceService` to a live implementation or documented HYBRID state. | `web/features/attendance/*` — see "AC05" below for the honest, precise claim |

## Design decisions and scope boundaries

### AC01: a genuinely new, plural shift model — not BIO-ATT-001's singleton policy

BIO-ATT-001's own `AttendancePolicy` is exactly ONE tenant-wide record (`ExpectedStartTime` +
`LateGraceMinutes`) and its own README explicitly disclosed overtime as out of scope ("a
`check_out`-paired duration computation this story's own AC set does not ask for"). This story's
own AC01 wording is plural ("shift schedule**s**"), a genuinely different shape: `WorkforceShift`
is a new, independent, many-per-tenant CAS-versioned aggregate (`Name`, `StartTime`, `EndTime`,
`GraceMinutes`), never a reinterpretation of BIO-ATT-001's own singleton policy. `ExpectedDurationMinutes`
wraps correctly past midnight for an overnight shift (e.g. `22:00`–`06:00` = 480 minutes), proven
in `OvertimeEvaluatorTests.An_overnight_shift_wraps_past_midnight_for_its_own_expected_duration`.

### AC02: clock pairs are validated, never silently guessed

BIO-ATT-001's own `AttendanceEvent` already distinguishes `CheckIn`/`CheckOut`/`Presence`
(AC01's own "mode"), but `AttendanceStatusEvaluator` only ever consults the day's first arrival —
`CheckOut` events are captured today but never consumed by anything. `ClockPairEvaluator` is the
first consumer: a pure function pairing each `CheckIn` with the next chronological `CheckOut`,
surfacing four distinct outcomes rather than forcing every event into "complete" —
`Complete`, `OpenCheckIn` (clocked in, never clocked out), `UnmatchedCheckOut` (no preceding
open check-in), `Overlapping` (a second check-in before the first was closed). An irregular
pairing is never silently merged into a complete one or guessed at — each is its own named,
auditable status. Proven directly in `ClockPairEvaluatorTests.cs` (8 tests, including
out-of-chronological-order input and multiple pairs in one day).

### AC03: overtime is derived, never a raw-event mutation

`OvertimeEvaluator.EvaluateMinutes` is a pure function: total worked minutes across every
`Complete` pair that day, minus the shift's own `ExpectedDurationMinutes`, floored at zero (a
short day is simply not overtime, never negative). Irregular pairs contribute no duration —
AC02's own validation already surfaces them separately; this evaluator does not also guess a
duration for them. Nothing here ever writes back to BIO-ATT-001's own `AttendanceEvent` store —
the identical "derived, never raw-event mutation" discipline `AttendanceStatusEvaluator` already
established for itself. Proven directly in `OvertimeEvaluatorTests.cs` (6 tests).

### AC04: exceptions require audit reason — the two-tier never-mutate shape

`WorkforceAttendanceAdjustment` mirrors BIO-ATT-002's own `AttendanceAdjustment` exactly: a
separate, immutable, append-only record layered ON TOP OF the computed result, never mutating it
and never touching a raw clock event. `Reason` is required and server-validated (non-empty, at
most 500 characters) — never optional, never defaulted; a request with a blank or missing reason
is rejected (`validation_failed`, 422) before anything is recorded. A daily summary read always
surfaces BOTH the originally-computed overtime (with its own grounding clock pairs) AND the
latest adjustment if one exists — `WorkforceDailySummary.EffectiveOvertimeMinutes` is what a
caller should act on, `ComputedOvertimeMinutes` is never silently overwritten or hidden. Creating
an adjustment is this feature's other authorization decision (ADR-0006 Decision 1/2, alongside
shift management) — gated by `IPermissionGateway` + `ActorProvenanceGate`, the identical pattern
BIO-ATT-002's own `AttendanceAdjustmentService` and this story's own `WorkforceShiftService` use.

### AC05: web surface

`web/features/attendance/ports.ts` gained a new `WorkforceAttendancePort` interface — additive
to the pre-existing generic `AttendancePort` (its own `primaryAction`/`flagException` shape is
untouched, same discipline BIO-TEN-003's own `TenantsPort` and BIO-INT-002's own
`IntegrationsPort` extensions established). Resolved directly against the `AttendanceService`
module's own feature-mode registry under a new `workforceAttendance` port key
(`web/lib/feature-mode.ts`'s `MODULE_PORT_KEYS`/`PER_PORT_ENV_DEFAULTS`) — not through the
generic `resolvePort<T extends ModulePort>` helper, since this port's shape has no
`primaryAction()` of its own.

`web/app/attendance/page.tsx` → `AttendancePage` gained a new `WorkforceAttendanceSection`,
rendered BELOW the existing "Attendance record"/"Flag exception" card — purely additive, no
redesign of the approved page, navigation or shared visual language. It renders: a shift
catalogue (load/create), a daily clock-pair/overtime summary lookup, and an exception-with-reason
form, each calling `workforceAttendancePort`'s real methods, never a raw `fetch` or a different
mock store directly.

**AC05: writes are genuinely live but cannot complete from the browser yet — a disclosed,
cross-cutting gap, not unique to this story.** Shift creation and adjustment creation are
authorization decisions (ADR-0006 Decision 1/2) and therefore require `ActorProvenanceGate`
(Decision 3) — but no browser-usable mechanism exists anywhere in this codebase yet for a
pre-authentication web page to produce a valid provenance proof. `web/features/attendance/live.ts`
sends real write requests (it does not fake success), so a write genuinely receives a 401 from
the real, correctly-secured backend today. This is the identical gap BIO-TEN-003's own round-2
remediation first discovered and disclosed (see `docs/features/bio-ten-003/README.md`'s own
"Round-2 disclosed HYBRID boundary") — god confirmed it is foundational and cross-cutting
(affecting any story pairing `ActorProvenanceGate` with a web, not mobile, surface) and is
tracking the real fix (a demo/dev provenance bridge) separately with Kemi/Emeka, not held on any
one story's own remediation round. **Reads are genuinely live end to end** — shift listing and
the daily clock-pair/overtime summary require no provenance (audit-attribution-only, the same
split BIO-ATT-001's own capture/read-vs-policy endpoints use) and work against the real backend
today without qualification.

`tests/frontend/playwright/bio-att-003-workforce-attendance-live.spec.ts` (new, opt-in via
`BIOLOG_ATT003_LIVE_TESTS=1`, same shape as `bio-ten-003-feature-configuration-live.spec.ts`)
proves exactly this: a real GET for the shift list succeeds end to end, and a real POST to create
a shift is correctly rejected (401) with the UI surfacing the failure rather than silently
succeeding. `web/next.config.mjs` gained its own `/api/v1/workforce-attendance/:path*` rewrite
entry, a deliberately separate prefix from BIO-ATT-001's own `/api/v1/attendance/*`.

## Round-2 remediation (2026-10-07)

Round 1 (Seyi) FAILed on two High findings. Both are closed in this round; see
`evidence/features/bio-att-003/builder/round2-metadata.json` for the full verification record.

### BIO-ATT-003-F01: lateness and absence are now interpreted, not just stored

The approved user story requires "clock-in/out, shift, lateness, overtime and absence
interpretation" - round 1 covered clock pairing and overtime only. `WorkforceShift.GraceMinutes`
had no consumer anywhere, and a day with no clock events was merely an empty pair list, never an
absence outcome.

`Domain/AttendanceInterpretationEvaluator.cs` (new, pure function, the same discipline
`ClockPairEvaluator`/`OvertimeEvaluator` already hold themselves to) derives
`AttendanceInterpretationStatus` (`OnTime`/`Late`/`Absent`) directly from the day's raw clock
events and the selected shift - reads raw events directly rather than `WorkforceClockPair`,
since lateness is about WHEN the first CheckIn happened, not whether it ever paired with a
CheckOut (an `OpenCheckIn`/`Overlapping` day still has a real arrival time to judge). Absent
means no CheckIn event exists at all for that date - a `Presence`-only or `CheckOut`-only day is
still Absent, never silently treated as present. Lateness math wraps across midnight ONLY for an
overnight shift, and only when doing so is genuinely the more plausible reading (see "Round-3
remediation" below for the exact fix - round 1's original unconditional-wrap version had a real
bug, closed in round 3) - proven directly in `AttendanceInterpretationEvaluatorTests.cs` (14
tests, including the reviewer's own 08:00+10min-grace/08:11-arrival reproduction, two overnight
cases, and the round-3 early-arrival regression cases).
Wired into `WorkforceAttendanceService.GetDailySummaryAsync` - `WorkforceDailySummary.Interpretation`
is now a required field, surfaced over the wire as `interpretation` (`status`,
`first_check_in_event_id`, `minutes_late`) and in the web UI below the clock-pair table.

### BIO-ATT-003-F02: AC05 now proves a real authorized workflow, not the obsolete pre-bridge boundary

This story was built before TD-WEB-PROVENANCE-BRIDGE's merge (`7ac4876`) finished, but that
merge had already landed before builder-complete - the disclosed-boundary/negative-only proof
this story originally shipped with was therefore already stale at submission, not a fix this
round introduces in response to a NEW event.

Two gaps had to be closed, found and fixed the same way BIO-TEN-003's own
TD-WEB-PROVENANCE-BRIDGE-REVERIFY round found its own (see that story's README for the parallel
account):

1. `web/lib/actor-provenance-routes.ts` had no entry at all for `/api/v1/workforce-attendance/*`
   - `computeActorProvenance` silently returned `null` regardless of session validity. Added
   (`WorkforceAttendance:ActorProvenanceSecret`), proven in
   `tests/frontend/unit/td-web-provenance-bridge-route-table.test.ts`.
2. The real RBAC engine needed a seeded grant for this story's own two permission keys.
   `BioAtt003Module.Configure` now seeds (fail-closed/inert unless
   `WebSessionBridge:SyntheticTenantId` is configured, idempotent, the same "seed data, not
   provisioning logic" shape `DemoSessionIdentitySeed` established) a real
   `RolePermissionGrant` for `TenantAdmin` -> `workforce_attendance.shift_manage` and
   `workforce_attendance.adjustment_manage`, in the one synthetic tenant TD-WEB-PROVENANCE-
   BRIDGE round 5 already provisions. **Simpler than BIO-TEN-003's own equivalent fix**: this
   story resolves tenant AMBIENTLY throughout (never an explicit route-supplied tenant id), so
   no separate tenant-registry seeding is needed - round 5's `DemoSessionTenantResolver`
   confining ambient resolution to the synthetic tenant is sufficient on its own.

`tests/dotnet/Biolog.IntegrationTests/BioAtt003ReverifyEndpointsTests.cs` (new, 3 tests) proves
a real admin session creates a shift (201, confirmed via a real follow-up list read), a real
viewer session (no `shift_manage` grant) is denied with a genuine 403 (not a blanket rejection -
reads stay open), and the pre-existing no-provenance 401 is unchanged. Rewrote
`tests/frontend/playwright/bio-att-003-workforce-attendance-live.spec.ts` to prove the same
workflow through a real browser - a signed admin session loads and creates a shift end to end, a
signed viewer session is correctly denied (403, UI surfaces the failure) - replacing the old
negative-only (401) assertions entirely. Unlike BIO-TEN-003's own live spec, this page never
auto-fetches on mount (every load is an explicit "Load shifts" click), so there was no cold-
start session/fetch race to work around here - verified clean across 4 consecutive manual runs.

### Found and fixed along the way: a self-introduced BIO-UX-005/BIO-UX-010 fixture regression

Running the full `tests/frontend test:unit` suite (not run for round 1's own submission - an
oversight corrected this round) surfaced that round 1's own addition of a `workforceAttendance`
port key to `MODULE_PORT_KEYS.AttendanceService` broke two invariant-checking fixture tests
(`bio-ux-005-module-registry.test.ts`'s registry/feature-mode cross-check, `bio-ux-010-module-
ports.test.ts`'s own hardcoded module list) that assume every `MODULE_PORT_KEYS` entry has a
matching `ModulePort`-shaped entry in `module-registry.ts`'s `MODULE_PORTS` - `workforceAttendancePort`
cannot satisfy that shape itself (no `primaryAction()`). Fixed by adding a thin `ModulePort`
proxy (`workforceAttendanceModulePort`, `web/features/attendance/index.ts`) whose own
`primaryAction()` genuinely calls `listShifts()` rather than fabricating an always-success
stub - `/attendance` is a real page, never `PlaceholderPage`-routed, so nothing actually
exercises this proxy at runtime; it exists solely to satisfy that registry invariant. Updated
both fixtures' hardcoded expectations to include the new key. Confirmed, before this fix, round
1's own evidence (`metadata.json`) claimed "same 2 pre-existing bio-ux-005 failures" without
having actually diffed the failure content - it was really 3 failures, one of them this story's
own regression, not merely the pre-existing, unrelated `IntegrationService`/`CredentialService`
gap (`TD-BIO-UX-005-MODULE-REGISTRY-FIXTURE-GAP`). Confirmed clean afterward: 543/545, same 2
pre-existing, unrelated failures only.

## Round-3 remediation (2026-10-07): SY-ATT003-03

Round 2's own F01 fix applied an UNCONDITIONAL `modulo 1440` to `arrival-minus-start`, so a
genuinely EARLY arrival (07:55 against an 08:00 start + 10 min grace, raw delta -5) wrapped to
1425 minutes "late" instead of being recognised as on time (Seyi round 2, High). The wrap is
only ever valid for an overnight shift's own early-morning continuation - a same-day shift has
nothing to wrap across at all, so a negative delta there is simply an early arrival.

Even restricted to overnight shifts, "wrap whenever negative" is still wrong: an overnight shift
can also have a genuinely early arrival the same evening, before its own start (21:50 against a
22:00-06:00 shift, delta -10) - bare clock-time-of-day cannot tell that apart from the unwrapped
reading of a genuinely-after-midnight arrival without more information than this evaluator has.
Fixed by gating the wrap behind `isOvernightShift` (mirrors `WorkforceShift.ExpectedDurationMinutes`'s
own `end <= start` condition) AND only applying it when the wrapped reading (`delta + 1440`) is
genuinely closer to zero than the unwrapped one - equivalent to wrapping only when the raw delta
is more negative than -720 (half of 1440, the exact crossover). This correctly resolves both the
reported case (same-day, never wraps at all) and the previously-untested overnight-early case
(wraps only when that is the more plausible reading).

Three new tests in `AttendanceInterpretationEvaluatorTests.cs` (now 14 total): the exact SY-ATT003-03
reproduction (07:55/08:00+10), a much-earlier same-day arrival (00:01 against an 08:00 shift,
still on time), and an overnight shift's own early-same-evening arrival (21:50 against
22:00-06:00, on time - not wrapped into a near-full-day-late reading). All prior overnight-late
tests (00:20/00:10 against 22:00/23:50 starts) still pass unchanged - the crossover-based fix is
strictly more correct, not a behaviour change for the cases round 2 already proved.

**Verification:** own suite 64/64 (was 61); full `Biolog.IntegrationTests` 230/230 unchanged;
`scripts/check-module-project-references.sh`/`check-contract-freeze.py` PASS; `dotnet build`
clean.

## API surface

Tenant-plane scoped via ambient `ICurrentTenantProvider` (never a route-supplied tenant id —
`/attendance` is a single tenant's own operator console, unlike BIO-TEN-003's cross-tenant admin
console). Versioned under `/api/v1` per ADR-0004 Decision 1.1.

| Method | Path | Notes |
| --- | --- | --- |
| `POST` | `/api/v1/workforce-attendance/shifts` | Requires provenance + `workforce_attendance.shift_manage`. |
| `GET` | `/api/v1/workforce-attendance/shifts` | Open — no actor required. |
| `GET` | `/api/v1/workforce-attendance/shifts/{shiftId}` | Open. |
| `PATCH` | `/api/v1/workforce-attendance/shifts/{shiftId}` | Requires `version` (CAS), provenance + `workforce_attendance.shift_manage`. |
| `GET` | `/api/v1/workforce-attendance/people/{personId}/daily-summary/{date}?shift_id=` | Open — requires `?shift_id=`. |
| `POST` | `/api/v1/workforce-attendance/people/{personId}/adjustments/{date}` | Requires provenance + `workforce_attendance.adjustment_manage`; body `reason` is required and validated. |

Every response carries `X-Correlation-Id`; every error is `application/problem+json` shaped.

## Configuration fields

Shift: `name`, `start_time`/`end_time` ("HH:mm"), `grace_minutes`, `expected_duration_minutes`
(computed, wraps past midnight), `version`. Daily summary: `pairs[]` (`status`,
`check_in_event_id`, `check_out_event_id`, `duration_minutes`), `computed_overtime_minutes`,
`effective_overtime_minutes`, `adjustment` (nullable: `adjustment_id`, `original_overtime_minutes`,
`adjusted_overtime_minutes`, `reason`, `adjusted_by_actor_id`, `adjusted_at`).

## Test counts

64 developer tests in `tests/features/bio-att-003/` (round 1: 46; round 2 adds 15; round 3 adds
3) — `ClockPairEvaluatorTests.cs` (8, AC02), `OvertimeEvaluatorTests.cs` (6, AC03),
`WorkforceShiftServiceTests.cs` (9, AC01), `AttendanceInterpretationEvaluatorTests.cs` (14, F01/
SY-ATT003-03), `WorkforceAttendanceServiceTests.cs` (10, AC02/AC03/AC04/F01), `ActorProvenanceGateTests.cs`
(5), `WorkforceAttendanceEndpointsIntegrationTests.cs` (12, AC01–AC05/F01 real HTTP pipeline) —
mapped to AC/finding IDs by test name and/or doc comment. Plus
`tests/dotnet/Biolog.IntegrationTests/BioAtt003ReverifyEndpointsTests.cs` (3, F02) - through
the real packaged host, real RBAC, real seeded grants. Zero regressions: BIO-ATT-001's own 24
tests pass unmodified, and the full `Biolog.IntegrationTests` suite (230 tests) passes with no
failures at all - the 2 pre-existing `AuthenticationEndpointsTests` failures flagged during round
1 are gone (Busola's `Authentication:SessionTokenSecret` fix landed and merged separately,
unrelated to this story). Also found and fixed, in round 2, a round-1 regression in
`tests/frontend test:unit`'s own `bio-ux-005`/`bio-ux-010` fixtures - confirmed clean at 543/545
(same 2 pre-existing, unrelated failures), unaffected by round 3.
