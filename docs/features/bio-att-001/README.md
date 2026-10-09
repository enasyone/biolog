# BIO-ATT-001 — Attendance event core

**Wave:** W10 / W10.1 · **Conflict group:** `attendance-core` · **Owner:** Tunde
**Dependencies:** BIO-IDN-001 (person registry), BIO-ARC-003 (audit/event envelope contracts)

## User story

As tenants, we need one attendance event model supporting education, workforce and presence
interpretations.

## AC → evidence map

| AC | Summary | Proven by |
|---|---|---|
| **AC01** | Event captures person, source/device, location, time, mode and confidence/verification evidence | `Domain/AttendanceEvent.cs`, `Domain/VerificationEvidence.cs`; `AttendanceEventServiceTests.cs` `AC01_*`; host proof `AttendanceHostChannelTests.cs` |
| **AC02** | Raw event is distinct from derived attendance status | `Domain/AttendanceEvent.cs` (no status field of any kind) vs. `Domain/DerivedAttendanceStatus.cs` (computed, never persisted) via `Domain/AttendanceStatusEvaluator.cs`; `AttendanceStatusEvaluatorTests.cs`, `AttendanceStatusServiceTests.cs` |
| **AC03** | Duplicate/idempotent capture is handled | `Domain/AttendanceEvent.IdempotencyKey`/`ContentFingerprint`, `AttendanceEventLockRegistry`, `AttendanceEventService.CaptureAsync`; `AttendanceEventServiceTests.cs` `AC03_*` (including a genuine concurrent-request race test) |
| **AC04** | Tenant policies define lateness/absence/attendance interpretation | `Domain/AttendancePolicy.cs`, `Domain/AttendanceStatusEvaluator.cs`; `AttendancePolicyServiceTests.cs`, `InMemoryAttendancePolicyRepositoryTests.cs` |
| **AC05** | `/attendance` is connected through `AttendanceService` to a live implementation, no unapproved UI redesign | See "Proving AC05" below |

## Why capture and read stay open, and policy does not (ADR-0006 Decision 2, re-derived — not assumed)

BIO-ACC-001's own QA-01 lesson: a sibling story's "open read" precedent is not automatically safe
to reuse — it has to be re-justified against what *this* story's own data actually discloses, not
pattern-matched on endpoint shape. Re-derived here from first principles:

- **Event capture and every read** (`POST /events`, `GET /events`, `GET /status`, `GET /policy`)
  are audit-attribution uses only (ADR-0006 Decision 1/2): any caller who can reach the tenant
  context can already capture or read attendance data regardless of who they claim to be — the
  identical shape BIO-IDN-001 through BIO-IDN-005 already establish for PERSON's own name/date-of-
  birth. A forged actor id changes who is blamed for a capture, not what the forger could already
  do. Attendance presence/time data is comparable sensitivity to PERSON's own core fields, not to
  BIO-ACC-001's access-policy rule content (named subjects, DENY rules, schedules — literally the
  blueprint for bypassing a tenant's physical security, which is why BIO-ACC-001's own reads
  *are* gated). So Decision 2's boundary does not require a provenance proof here.
- **`PUT /policy`** changes how *every* person's raw events in the tenant get classified going
  forward — a capability, gated by identity (ADR-0006 Decision 1/2), not an audit-attribution
  record. It requires both `attendance.policy_manage` (via `IPermissionGateway`, wrapping
  BIO-IDN-006's `PermissionService.ActorHasPermissionAsync`) **and** a verified ADR-0006
  Decision 3 provenance proof (`Api/ActorProvenanceGate.cs`, hand-ported feature-locally from
  BIO-ACC-001's own class — same HMAC-bound-to-actor-id construction, same
  `Attendance:ActorProvenanceSecret` out-of-band configuration key pattern, own secret). A
  permission check alone, keyed on an unverified `X-Actor-Id`, would be exactly BIO-IDN-006 Round
  1's mistake (`ZA-IDN006-01`).

## Raw vs. derived (AC02) — the actual mechanism

`AttendanceEvent` has no status field of any kind — not a compile-time coincidence, a structural
guarantee (`tests/features/bio-att-001/BIO-ATT-001.test.sh` greps for and fails on one appearing).
`AttendanceStatusEvaluator.Evaluate` is a pure function: `(tenantId, personId, date, events,
policy, now) -> DerivedAttendanceStatus`. No repository, no audit sink, no I/O. The same raw event
set classifies differently under two different policies
(`AC02_and_AC04_together_the_identical_raw_event_set_classifies_differently_under_two_different_policies`)
and setting a new policy after a capture never touches the already-stored raw event
(`AttendanceStatusServiceTests.cs`) — this is the concrete proof that "derived" really is
computed, not cached-and-forgotten.

This mirrors BIO-IDN-002's "refuse auto-flip for time-based lifecycle status; compute in effect at
read time" discipline for an identical reason: no background scheduler infrastructure exists in
this codebase to keep a stored derived value from silently going stale.

## Idempotent capture (AC03) — mechanism and its disclosed boundary

`IdempotencyKey` is producer-minted (the device/client mints and remembers it — e.g. per local
sequence number). `ContentFingerprint` is a SHA-256 over every business field *except*
`EventId`/`ReceivedAt` (both server-assigned per delivery, never per content) — the identical
exclusion ADR-0003 Decision 4 makes for its own redelivery fingerprint, for the identical reason.

- Same key, same content → answered idempotently (the *original* event returned, no new insert,
  no new audit entry).
- Same key, different content → `409 idempotency_conflict`, never a silent overwrite.
- Capture is serialized per `(TenantId, IdempotencyKey)` via `AttendanceEventLockRegistry`
  *before* any validation runs, so two concurrent identical retries cannot both observe "no prior
  event" and both insert — proven with a genuine 20-way `Task.WhenAll` race
  (`AC03_c_concurrent_captures_with_the_same_idempotency_key_serialize_and_only_one_event_is_ever_created`),
  not just a sequential re-check.

**Disclosed scope boundary**, stated in `Domain/AttendanceEvent.cs`'s own header: this defends
against *redelivery* of the same logical capture (a device retrying its own already-sent tap). It
does not attempt to detect two events with two *different* idempotency keys describing the same
physical occurrence (e.g. two independent devices both reporting one tap) — a device/deployment-
topology concern outside a single capture endpoint's own reach, the same "disclosed, not silently
absorbed" boundary BIO-ACC-001 recorded for its own caller-supplied evaluation inputs.

Note also: ADR-0003 Decision 4's own dedupe defends against *redelivery of the same audit/domain
event envelope*. This feature's `IdempotencyKey`/`ContentFingerprint` mechanism is a right-sized,
feature-local port of that same *pattern* (fingerprint the content, exclude server-stamped
fields, reject a mismatched reuse as a conflict) applied to this feature's own natural key — not a
literal reuse of the shared contract's own dedupe machinery, the same "hand-ported, not shared
library" relationship every `.NET` feature in this codebase has with `contracts/js/audit.mjs`
(see `Audit/AttendanceAuditEvent.cs`'s own header).

## Tenant interpretation policy (AC04)

`AttendancePolicy` is a per-tenant, CAS-versioned mutable record (natural key: `TenantId` alone —
exactly one policy per tenant). `InterpretationMode` covers `education`/`workforce`/`presence`
(MASTER-TECHNICAL-SPECIFICATION.md §10: "Attendance uses immutable source events with
education/workforce/presence interpretations"). A tenant with no explicit policy still has an
effective one (`AttendancePolicy.Default`: `presence` mode, no expected start time — any
`check_in`/`presence` event that day is simply `present`, since lateness cannot be judged against
nothing). Setting a policy is compare-and-set against the currently-stored version
(`IAttendancePolicyRepository.TrySaveAsync`), proven directly at the repository level
(`InMemoryAttendancePolicyRepositoryTests.cs`) since the service's own lock always serializes and
re-reads before writing, so the service itself cannot naturally trigger a version mismatch in a
single-process test — the CAS is defense-in-depth against a hypothetical second writer bypassing
that lock (e.g. a future multi-process deployment), and its mechanism is verified at the point
that actually exercises it.

## Person existence gateway — the established identity-wave pattern, applied unchanged

`Domain/IPersonExistenceGateway.cs` is the same narrow, read-only seam every identity-wave story
establishes for itself (never a `ProjectReference`). `Biolog.Api`'s `BioAtt001Module` implements it
by wrapping BIO-IDN-001's own already-registered `PersonService.GetAsync` directly. Deliberately
does **not** also check lifecycle status: attendance capture records a physical occurrence that
already happened, and refusing to record it because the person is administratively suspended would
silently destroy the very fact a suspension investigation might need. Gating *who may be granted
access* on suspension status is BIO-ACC-001's job, not this story's — a disclosed scope boundary,
stated in that interface's own header, not an oversight.

## Device — shape-validated only, not existence-gated

`device_id` is required exactly when `source` is `device`, forbidden otherwise
(`AttendanceEvent.Capture`'s own validation). It is shape-checked against the canonical `dev_`
identifier pattern only — **not** existence-gated against BIO-DEV-001's device registry, because
BIO-DEV-001 is not a declared dependency of this story (only BIO-IDN-001 and BIO-ARC-003 are). The
identical "an undeclared dependency's value stays caller-supplied, never gateway-resolved"
discipline BIO-ACC-001 applied to credential class, location and profile key.

## Proving AC05 — connected, not redesigned

`web/features/attendance/{attendance-page.tsx,ports.ts,mock.ts}` are **completely untouched** by
this story (a structural check in `BIO-ATT-001.test.sh` fails if any of them are ever edited under
this story's own attribution). `web/features/attendance/live.ts` — BIO-UX-010's own scaffolding —
already POSTed a bare, headerless request to `/api/v1/attendance/primary-action` and
`/api/v1/attendance/flag-exception`, exactly per its own doc comment: "each calls its own real
backend endpoint this domain's story wires up; until then both genuinely fail closed to offline."
This story needed **zero changes to that file**: only (1) `web/next.config.mjs` gained one more
`rewrites()` entry forwarding `/api/v1/attendance/:path*` to `Biolog.Api` (the identical
BIO-DOC-009-AC06 pattern, one more array entry, no other route touched), and (2)
`Api/AttendanceEndpoints.cs` maps those exact two routes for real.

**Why a dedicated `AttendanceDemoBridgeService`, not the tenant-scoped application services
directly.** `web/features/attendance/live.ts`'s `fetch()` call carries no custom headers at all —
by design, since no login/session story exists yet (identical reasoning to BIO-DOC-009's own
`AuthenticityEndpoints.ComputeAsync`/`ActorResolver.ResolveOrDefault`, ported here as a dedicated
bridge rather than a resolve-or-default variant, since this feature's real capture endpoint needs
a genuinely *resolved* tenant, not merely a fallback actor id). `AttendanceEventService`/
`AttendancePolicyService` require a real `ICurrentTenantProvider.TenantId`, which a headerless demo
POST resolves to `null`. `AttendanceDemoBridgeService` is registered Singleton, depends only on the
Singleton repositories/audit sink directly (no captive-dependency risk — neither has a Scoped
dependency of its own), and calls the exact same `AttendanceEvent.Capture`/
`AttendanceStatusEvaluator.Evaluate` domain statics against one fixed, already-public SYNTHETIC
demo tenant/person/device set (the tenant id is `contracts/schemas/identifier-registry.json`'s own
published `tenant` example). This is a real, connected live implementation of the two existing
mapped actions — not a second mock: `POST /primary-action` genuinely captures (or idempotently
replays) a check-in and returns the freshly-**computed** derived status; `POST /flag-exception`
writes a real audit entry. Proven end-to-end with **zero headers**, matching the real UI caller
exactly, in `AttendanceHostChannelTests.cs`'s
`ATT001_AC05_the_two_UI_bridge_routes_answer_a_genuinely_headerless_request_through_the_real_host`.

A real bug was caught by this exact test before it ever reached review: the bridge's first draft
passed `source: AttendanceSource.Device` with `deviceId: null`, which `AttendanceEvent.Capture`'s
own AC01 validation correctly rejects (`device_id is required when source is 'device'`) — a
`422` on every single call, silently swallowed by nothing (the demo route has no
try/catch-and-ignore). Caught by curling a real dev server rather than guessing, fixed by adding a
fixed demo `dev_` device id, verified by rerunning the exact same host-channel test.

## Audit trail

`Audit/AttendanceAuditEvent.cs` is a right-sized, feature-local hand-port of ADR-0003's audit
envelope (mirrors BIO-IDN-001's own `PersonAuditEvent.cs` — no generated `.NET` binding of
`contracts/js/audit.mjs` exists yet, so every `.NET` feature in this codebase ports the envelope
itself). Every successful capture and every policy update writes one audit entry
before the repository commit is treated as complete (ADR-0003 Decision 7's audit-atomicity
pattern); a pure idempotent replay writes **no** new audit entry (nothing new happened).

## Deliberately out of scope

- **Device existence/status gating** — see "Device" above.
- **Suspension/lifecycle gating on capture** — see "Person existence gateway" above.
- **Tenant timezones.** `AttendancePolicy.ExpectedStartTime` is tenant-local wall time with no
  timezone attached; every comparison in `AttendanceStatusEvaluator` works in the same convention
  as `AttendanceEvent.OccurredAt`'s own UTC clock. This story does not model tenant timezones — a
  disclosed simplification, not a defect; a later story that needs one extends
  `AttendancePolicy` when it has an actual use for it.
- **Overtime.** MASTER-TECHNICAL-SPECIFICATION.md §10 names "present/late/overtime" as an example
  of policy-driven derived status; this story's own AC04 wording is narrower ("lateness/absence/
  attendance interpretation"). `AttendanceStatusValue` is `Present`/`Late`/`Absent` — overtime
  needs a `check_out`-paired duration computation this story's own AC set does not ask for;
  flagged here rather than silently invented, the same "don't invent an unspecified pattern"
  discipline the BIO-UX wave's own design-system reminder established.
- **BIO-ATT-002/003's own surfaces.** `governance/ui-integration-map.json` maps all three
  `BIO-ATT-00x` stories to the same `/attendance` route and `AttendanceService` port
  (`mode_transition: "SIMULATION/HYBRID -> LIVE"`) — this story connects exactly the two actions
  that already exist on that page today; richer capture/policy UI is a later story's own AC, not
  retrofitted here.

## Verification

```
dotnet build api/Biolog.Api/Biolog.Api.csproj                                            0 errors
dotnet test tests/features/bio-att-001/Biolog.Features.Attendance.Tests.csproj           24/24
dotnet test tests/dotnet/Biolog.IntegrationTests/Biolog.IntegrationTests.csproj          134/134 (was 128; +6 AttendanceHostChannelTests)
bash tests/features/bio-att-001/BIO-ATT-001.test.sh                                      33/33
scripts/quality.sh                                                                        QUALITY PASS
```
