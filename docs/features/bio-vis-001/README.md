# BIO-VIS-001 — Implement visitor preregistration and host approval

**Owner:** Tunde — Identity & Tenant Engineer
**Wave:** W10 · build batch W10.2 · conflict group `visitor-core`
**Dependencies:** BIO-IDN-001 (person lifecycle), BIO-IDN-006 (RBAC)

## AC → evidence map

| AC | Summary | Proven by |
|---|---|---|
| AC01 | Visitor record links to PERSON/profile where appropriate, without forcing permanent identity for every lead | `Domain/VisitorRecord.cs` (`PersonId` nullable), `VisitorRecordService.PreregisterAsync` (existence gateway when present), `VisitorRecordServiceTests.cs` AC01_a-d |
| AC02 | Host approval states are auditable | `Domain/VisitorEnums.cs` (`HostApprovalStatus`), `VisitorRecordService.ApproveAsync`/`DenyAsync`, every transition audited, `VisitorRecordServiceTests.cs` AC02_a-f |
| AC03 | Configurable fields include vehicle, NDA, safety briefing, watchlist and temporary credential options | `Domain/VisitorTenantConfig.cs`, enforced at check-in by `VisitorRecordService.CheckInAsync`, `VisitorRecordServiceTests.cs`/`VisitorConfigServiceTests.cs` AC03_a-d |
| AC04 | Automatic expiry is supported | `Domain/VisitorStatusEvaluator.cs` (read-time only, never persisted), `VisitorStatusEvaluatorTests.cs`, `VisitorRecordServiceTests.cs` AC04_a-b |
| AC05 | `/visitors` connected through `VisitorService` to this story's live implementation or documented HYBRID | `Demo/VisitorDemoBridgeService.cs`, `Api/VisitorEndpoints.cs`, `web/next.config.mjs` rewrite, `VisitorHostChannelTests.cs` |

## Architecture summary

Two aggregates, mirroring BIO-ATT-001's own split between an immutable fact and a mutable config:

- **`VisitorRecord`** (`Domain/VisitorRecord.cs`) — a mutable, CAS-versioned lifecycle aggregate (the same "internal constructor, internal mutation methods, `with`-expression transitions, per-record lock, dual-boundary transition validation" shape BIO-IDN-001's `Person` established). Carries identity/contact/host fields, the AC03 configurable-evidence fields (vehicle, NDA, safety briefing, watchlist result, temporary credential), a `HostApprovalStatus` (Pending/Approved/Denied) and a `VisitStatus` (Preregistered/CheckedIn/CheckedOut).
- **`VisitorTenantConfig`** (`Domain/VisitorTenantConfig.cs`) — a per-tenant CAS-versioned mutable record, the identical shape to BIO-ATT-001's own `AttendancePolicy`. Governs which AC03 fields are required before check-in, whether temporary credentials are allowed, and the default/max visit-duration window AC04's expiry is computed from.

Two services: `VisitorRecordService` (preregistration, approve/deny, check-in/check-out, evidence capture) and `VisitorConfigService` (get/set the tenant config) — both `internal`, DI-registered by `api/Biolog.Api/Modules/BioVis001Module.cs`, the one-way-dependency module pattern every prior story in this codebase uses.

## Why VisitorRecord has no canonical `vis_` id prefix

`contracts/schemas/identifier-registry.json` (Kemi-owned, ADR-0001 Decision 1, "adding or changing an entry requires an amending ADR and recorded Kemi approval") has no `vis`/visitor entry — only `ten`/`per`/`crd`/`dev`/`dcs`/`evt`. Minting a new registered prefix unilaterally would be exactly the kind of shared-contract change this story is not authorized to make on its own.

BIO-ACC-001 already faced the identical situation for its own new aggregate (`AccessPolicy`) and resolved it by keying the aggregate on a business `PolicyKey` string rather than requesting a new canonical type. `VisitorRecord` needs a system-generated identity (many preregistrations per tenant, not a small set of named keys), so the equivalent move here is a plain, undecorated `Guid.CreateVersion7()` string (`Identifiers/CanonicalId.NewVisitorId()`) — never a `vis_`-prefixed value, so nothing in an audit trail, API response or log could be mistaken for a value the identifier registry actually recognises. `per_`/`ten_` shape validation and `evt_` minting (for audit event ids and correlation ids — `evt` *is* a registered type) are unaffected.

## Why preregistration/check-in/check-out stay open, and approval/config do not

Re-derived from this story's own data, not pattern-matched from a sibling story's endpoint shape (BIO-ACC-001's own QA-01 lesson, already re-applied correctly once by BIO-ATT-001):

- **Preregistration, listing, reads, check-in/out, and the evidence-capture actions** (NDA acceptance, safety briefing, watchlist result, temporary-credential issuance) disclose visitor contact/vehicle/screening data — comparable sensitivity to BIO-ATT-001's own presence data and to PERSON's own name/date-of-birth (both of which every identity-wave story from BIO-IDN-001 onward leaves open to any asserted actor, audit-attribution only). These stay open: ADR-0006 Decision 2.
- **`Approve`/`Deny`** are this feature's one true authorization decision beyond config: deciding host approval is deciding who is granted physical premises access. Gated by `VisitorRecordService.DecidePermission` (`"visitor.approve"`) **and** a verified ADR-0006 Decision 3 provenance proof.
- **`VisitorConfigService.SetAsync`** changes what every future preregistration in the tenant requires before check-in — a capability, gated by identity, the identical reasoning `AttendancePolicyService.SetAsync` already established for BIO-ATT-001. Gated by `"visitor.config_manage"` + provenance.

## AC03 in practice: required fields are enforced at check-in, not preregistration

A visitor is preregistered first (often before they even know exactly when they'll arrive); NDA signing, a safety briefing, a vehicle plate and watchlist screening are typically satisfied separately, sometime before arrival. `VisitorTenantConfig`'s four `Require*` flags are therefore checked by `VisitorRecordService.CheckInAsync`, not by `PreregisterAsync` — a tenant that requires NDA acceptance blocks check-in (not preregistration) until `RecordNdaAcceptanceAsync` has run.

One rule is **not** configurable away: a `WatchlistScreeningResult.Flagged` result blocks check-in unconditionally, regardless of `RequireWatchlistScreening`. A tenant's config governs which evidence is *required* before check-in, never whether a positive flag can be waved through.

## AC04: automatic expiry, computed at read time, never auto-mutated

The same "refuse auto-flip for time-based status when no background scheduler exists — compute in effect at read time" discipline BIO-IDN-002 established and BIO-ATT-001 already applied. `VisitorStatusEvaluator.EffectiveStatus(record, now)` is a pure function returning a genuinely distinct enum type (`EffectiveVisitStatus`, which — unlike the persisted `VisitStatus` — has an `Expired` member) so a reviewer can see at the type level, not just by convention, that `Expired` can never be written back to a stored record. `CheckInAsync` calls this evaluator and rejects an expired preregistration with `expired`; nothing about the stored record itself ever changes just because time has passed.

`ExpiresAt` is set once at preregistration: the tenant config's `DefaultVisitDurationHours` unless the caller supplies an explicit value, which must be in the future and no later than `MaxVisitDurationHours` out. `PreregisterAsync` correctly refuses a past `expires_at` — you cannot preregister a visitor whose visit has already expired — so the two tests proving AC04's read-time behaviour (`VisitorRecordServiceTests.AC04_a/b`) construct an already-expired record directly through the internal domain factory and insert it straight into the repository, the legitimate scenario being tested being an in-flight preregistration that ages past its own expiry before anyone checks in.

## AC05: `/visitors` — LIVE domain, HYBRID display (same disclosed pattern as BIO-ATT-001)

`web/features/visitors/{visitors-page.tsx,ports.ts,mock.ts}` are **completely untouched** by this story (harness-enforced, `BIO-VIS-001.test.sh`'s own check greps for zero `BIO-VIS-001` mentions in those three files). `live.ts` already existed as BIO-UX-005/010 scaffolding, POSTing headerless requests to three fixed routes:

- `/api/v1/visitors/primary-action` ("Check in visitor")
- `/api/v1/visitors/check-out-visitor` ("Check out visitor")
- `/api/v1/visitors/view-visit-history` ("View visit history")

This story adds the real `Biolog.Api` endpoints those three routes now resolve to, backed by `Demo/VisitorDemoBridgeService.cs` — a Singleton bridge that exercises this story's *real* `VisitorRecord`/`VisitorStatusEvaluator` domain code (preregister → auto-approve → check-in/check-out, cycling three fixed synthetic demo visitors) rather than a canned string, since `live.ts` sends no auth headers and no login story exists yet (BIO-INT-001, W12). The demo tenant id is the literal `tenant` example `contracts/schemas/identifier-registry.json` itself publishes — the same synthetic value BIO-ATT-001's own bridge already uses; host person ids reuse BIO-ATT-001's own three synthetic demo person ids. No collision risk: this feature's `IVisitorRecordRepository` is a completely separate in-memory store from BIO-ATT-001's event repository.

**What "HYBRID" means here, precisely:** the full preregistration/host-approval/config domain model is real, gated correctly, and reachable over HTTP (LIVE). The `/visitors` page's own *displayed* visitor list stays sourced from `web/lib/synthetic-data` — there is no preregistration/approval UI in the approved page yet (that would be an unapproved redesign, explicitly out of scope). Only the three named actions the approved page already exposes are wired to the live backend.

## Verification

```
dotnet build api/Biolog.Api/Biolog.Api.csproj                                  Build succeeded
dotnet test tests/features/bio-vis-001/Biolog.Features.Visitors.Tests.csproj   25/25 passed
dotnet test tests/dotnet/Biolog.IntegrationTests/Biolog.IntegrationTests.csproj (incl. 6 new VisitorHostChannelTests)
bash tests/features/bio-vis-001/BIO-VIS-001.test.sh                            31/31 passed
scripts/quality.sh
```

**A pre-existing, disclosed flake, unrelated to this story:** `Biolog.IntegrationTests.ObservabilityTests.AC01_a_real_request_logs_correlation_and_tenant_scope_to_the_console_view` (owned by BIO-FND-008, not this story) intermittently fails when the full suite runs under xUnit's parallel test execution — observed twice with two *different* exception shapes at the same line (`Assert.Equal` mismatch once, `StringBuilder.ToString()` `ArgumentOutOfRangeException` another time), the textbook signature of a data race, not a logic bug. Root cause: the test globally redirects `Console.Out` into a plain `StringWriter` (not thread-safe) while other test classes run concurrently and also write to console. Confirmed via isolation: `dotnet test --filter "FullyQualifiedName~ObservabilityTests"` passes 9/9 every time; a subsequent full-suite run passed 146/146 clean. Not touched — `ObservabilityTests.cs` is outside this story's write scope, and the fix (a per-test-isolated console/logger seam) belongs to whoever owns that test file.

## Deliberately out of scope

- No real BIO-CRD integration for `TemporaryCredentialIssued` — it is a flag this story's own domain sets after `HostApprovalStatus == Approved` and the tenant config allows it; issuing a physical/logical credential through BIO-CRD-00x is a downstream concern, not a declared dependency of this story.
- No watchlist-source integration — `WatchlistScreeningResult` is caller-supplied evidence only, the same disclosed-boundary style BIO-ATT-001 applied to `device_id`.
- No UI for preregistration/approval — `/visitors` remains the approved BIO-UX-010 page; only its three existing named actions are connected live (see AC05 above).
