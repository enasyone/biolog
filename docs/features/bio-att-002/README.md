# BIO-ATT-002 — Implement education attendance policies

**Owner:** Tunde — Identity & Tenant Engineer
**Dependency:** BIO-ATT-001 (attendance event core), DONE.

## User story

As education tenants, I need lecture/class/exam/hostel/event attendance interpretation.

## AC → evidence map

| AC | Statement | Where it lives |
| --- | --- | --- |
| AC01 | Session/roster association is supported. | `Domain/Session.cs` (CAS-versioned, embeds a roster of person ids), `SessionService.cs` |
| AC02 | Present/late/absent rules are configurable. | `Domain/SessionAttendancePolicy.cs` (per tenant **and** session type — richer granularity than BIO-ATT-001's single tenant-wide policy), `Domain/SessionAttendanceClassifier.cs` |
| AC03 | Evidence source is preserved. | `Domain/AttendanceEvidenceRecord.cs` carried unmodified from BIO-ATT-001's own events through to every roster read |
| AC04 | Manual adjustments are audited. | `Domain/AttendanceAdjustment.cs` (append-only, never mutates the computed classification), `AttendanceAdjustmentService.cs` |
| AC05 | `/attendance` connected through `AttendanceService`. | Takes over `primary-action`/`flag-exception` from BIO-ATT-001's own (removed) bridge — see below. |

## Design decisions and scope boundaries

### Session/roster is a CAS-versioned aggregate, not an event

A roster is a live enrolment list an administrator may revise between scheduling and the session
taking place — a mutable, optimistic-concurrency aggregate (ADR-0001 §3.1/§3.2), the same shape
BIO-ATT-001's own `AttendancePolicy` and BIO-TRN-001's own Route/Vehicle/Stop use, not an immutable
fact like BIO-ATT-001's own `AttendanceEvent`. Session/roster create and roster-update stay OPEN
(no ADR-0006 gate) — the same "only the one genuine authorization-bearing action per story gets a
permission gate" precedent BIO-TRN-001 established for its own Route/Vehicle/Stop CRUD.

### AC02: a genuinely richer policy than BIO-ATT-001's own

BIO-ATT-001's `AttendancePolicy` has exactly one policy per tenant — a single `LateGraceMinutes`
threshold shared by every capture regardless of occasion. This story's own AC02 wording —
"present/late/absent rules are **configurable**" — is satisfied by keying the policy on
`(TenantId, SessionType)`: an exam and a hostel roll-call can have different lateness/absence
thresholds for the same tenant. `SessionAttendancePolicy` also adds a second threshold,
`AbsentAfterMinutes` (arrival strictly after this is `Absent` rather than `Late`), that BIO-ATT-001's
own single-threshold policy has no room for. Setting this policy IS an authorization decision
(`attendance.session_policy_manage`, ADR-0006-gated) — it changes classification for every session of
that type in the tenant going forward.

### AC01/AC03: evidence is an input, never re-derived or discarded

`SessionAttendanceClassifier` is a pure function — `(session, policy, one person's raw evidence) →
SessionMemberClassification` — mirroring BIO-ATT-001's own `AttendanceStatusEvaluator` exactly
(same read-time-only discipline, same "the identical evidence classifies differently under two
different policies" proof). It reads BIO-ATT-001's own raw `AttendanceEvent`s through this story's
own `IAttendanceEventQueryGateway`, projected into a local `AttendanceEvidenceRecord` (event id,
occurred-at, source, mode, verification method/verified/confidence) — every field a roster reader
needs to see exactly how a classification was grounded is carried through untouched (AC03), never
flattened into a bare status.

### AC04: manual adjustments never mutate anything

`AttendanceAdjustment` is a separate, append-only record layered ON TOP of a computed
`SessionMemberClassification` — the identical two-tier shape `bio-doc-009`'s own
`AuthenticityManualOverride` established: create one, never overwrite or delete the original. A
roster read always shows BOTH the originally-computed status (with its own grounding evidence) and
the latest adjustment, if any (`SessionRosterMemberView.EffectiveStatus` picks the adjustment when
one exists, but `Classification.Status` — the computed value — is still there, untouched, for anyone
reading the audit trail). Recording an adjustment IS this feature's second authorization decision
(`attendance.session_adjustment_manage`, ADR-0006-gated) — it changes what every subsequent reader
sees as that person's effective status.

### Why this story edits BIO-ATT-001's own endpoint mapping

`governance/ui-integration-map.json` maps all three `BIO-ATT-00x` stories to the same `/attendance`
route and `AttendanceService` port. BIO-ATT-001's own README already named its two bridge routes
("View attendance record"/"Flag exception") as standing in for richer capture/policy UI a later
story would build — not retrofitted there by design. Those two actions are now far better served by
this story's own session/roster interpretation (a roster's present/late/absent breakdown, and a real
audited adjustment) than by BIO-ATT-001's flat single-person daily status, so this story takes them
over, the identical disclosed-handoff shape BIO-TRN-001 → BIO-TRN-002 already established:

- `api/features/bio-att-001/Api/AttendanceEndpoints.cs` — the `/primary-action`/`/flag-exception`
  route mappings and their two handler methods were removed, not left duplicated; its own `/events`,
  `/status` and `/policy` routes (this feature's real, permanent surface) are untouched.
- `api/features/bio-att-001/Demo/AttendanceDemoBridgeService.cs` — deleted (its two routes moved, and
  nothing else referenced it).
- `api/Biolog.Api/Modules/BioAtt001Module.cs` — its now-dangling `AttendanceDemoBridgeService`
  Singleton registration and `Demo` using were removed.
- `tests/features/bio-att-001/BIO-ATT-001.test.sh` — its AC05 checks were updated to assert the two
  routes are NOT mapped there any more (while `/events`/`/policy` still are), and that the deleted
  demo bridge file stays deleted.
- `tests/dotnet/Biolog.IntegrationTests/AttendanceHostChannelTests.cs` — its own AC05 host-channel
  proof for the two bridge routes was removed (moved to this story's own
  `AttendanceSessionsHostChannelTests.cs`).

`web/features/attendance/live.ts` is UNCHANGED — it already called exactly these two routes
(BIO-UX-010's own scaffolding); this story just re-points which module answers them, the same "no
redesign, just re-pointed" precedent BIO-TRN-002 established. `web/next.config.mjs`'s own
`/api/v1/attendance/*` proxy rule (added by BIO-ATT-001) already covers both routes unchanged.

### AC05's demo bridge: a genuine cross-story WRITE into BIO-ATT-001's own event store

`AttendanceSessionDemoBridgeService` is registered Singleton (a headerless UI POST resolves no
tenant context, so it cannot use this feature's own tenant-scoped, Scoped services). It needs BOTH a
demo `Session` (this feature's own aggregate — no cross-story concern) AND real attendance evidence
to classify against, which only exists in BIO-ATT-001's own event store. `IAttendanceDemoSeedGateway`
is a SEPARATE Singleton-safe adapter (not the tenant-scoped, Scoped `IAttendanceEventQueryGateway`)
wrapping BIO-ATT-001's own Singleton `IAttendanceEventRepository` directly and calling its own
internal `AttendanceEvent.Capture` factory — a real, disclosed exception to "gateways are read-only",
justified the same way BIO-TRN-002's own `ITransportDemoSeedProvisioner` is: the demo bridge has no
other way to produce evidence for a roster view to classify. Kept as two separate
classes/registrations from the start (not one adapter awkwardly split later) — the exact
captive-dependency bug (a Singleton depending on a Scoped service) BIO-TRN-002's own build caught and
fixed mid-story.

## Deliberately out of scope

- **Tenant timezones.** `Session.ScheduledStartTime` is tenant-local wall time with no timezone
  attached, the same disclosed simplification BIO-ATT-001's own `AttendancePolicy.ExpectedStartTime`
  already carries.
- **Roster membership validation beyond existence.** A roster person id must exist in the resolved
  tenant (`IPersonExistenceGateway`); this story does not check lifecycle/suspension status on
  roster add, the same BIO-ATT-001-established boundary ("recording a fact/enrolment should not
  silently fail because of an administrative status change elsewhere").
- **A device/reader existence gateway.** No AC in this story names a reader/gate dependency; the
  demo bridge's fixed `dev_` device id is shape-only, never existence-checked, the same disclosed
  boundary BIO-ATT-001's own `AttendanceEvent.DeviceId` already carries.
- **Overtime / duration-based statuses.** `SessionAttendanceStatusValue` is
  `Present`/`Late`/`Absent` only, the same scope BIO-ATT-001's own `AttendanceStatusValue` chose for
  the identical reason (this story's own AC set does not ask for one).

## Verification

```
dotnet build api/Biolog.Api/Biolog.Api.csproj                                                   0 errors
dotnet test tests/features/bio-att-002/Biolog.Features.AttendanceSessions.Tests.csproj
dotnet test tests/features/bio-att-001/Biolog.Features.Attendance.Tests.csproj                  (no regression)
dotnet test tests/dotnet/Biolog.IntegrationTests/Biolog.IntegrationTests.csproj
bash tests/features/bio-att-002/BIO-ATT-002.test.sh
bash tests/features/bio-att-001/BIO-ATT-001.test.sh                                              (no regression)
bash scripts/quality.sh                                                                          QUALITY PASS
```
