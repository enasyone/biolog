# BIO-ACC-002 — Implement online access decision service

**Wave:** W11 / W11.3 · conflict group `access-decision`
**Owner:** Tunde — Identity & Tenant Engineer
**Dependencies:** BIO-ACC-001 (access policy model and evaluator), BIO-BIO-004 (fingerprint verification), BIO-BIO-005 (face verification)

## User story

As readers/gates, I need a decision service that combines identity, credential, biometric evidence
and policy.

## AC → evidence map

| AC | What it asks | Where it's proven |
|---|---|---|
| AC01 | Face/fingerprint match is evidence, not the final allow decision | `AccessDecisionService.DecideAsync` gathers biometric evidence via `IFingerprintEvidenceGateway`/`IFaceEvidenceGateway` FIRST, derives `AssuranceLevel` via the pure `AccessDecisionCombiner.DeriveAssuranceLevel`, THEN calls `IPolicyDecisionGateway` — its `Decision`/`ReasonCode` is the sole source of ALLOW/DENY; `AccessDecisionCombinerTests.AC01_evidence_is_never_itself_the_allow_deny_decision`, `AccessDecisionServiceTests.AC01_a_matched_biometric_never_overrides_a_policy_deny_...` |
| AC02 | Suspended/revoked identity/credential denies | Inherited entirely from BIO-ACC-001's own `PolicyEvaluationService`/`PolicyEvaluator` (deny-precedence) — this feature never resolves identity/credential status itself, see "Integration seam" below; `AccessDecisionServiceTests.AC02_a_deny_reason_the_policy_gateway_reports_for_a_suspended_or_revoked_identity_flows_through_unchanged` |
| AC03 | Decision records policy version and reason codes | `Domain/AccessDecisionEvent.cs` (`PolicyKey`/`PolicyVersionNumber`/`ReasonCode`/`MatchedRuleId`, always populated, even for a "no published policy" deny), audited via ADR-0003 Decision 7's audit-atomicity pattern; `AccessDecisionServiceTests.AC03_the_decision_records_the_policy_key_version_and_matched_rule` |
| AC04 | Service supports deterministic replay in tests | `AccessDecisionCombiner.Combine`/`.DeriveAssuranceLevel`/`.ComputeFingerprint` are pure — no I/O, no clock read; `AccessDecisionCombinerTests.AC04_the_same_inputs_replayed_produce_a_structurally_identical_decision`; a caller-level idempotency key also replays the same decision without re-calling the biometric gateways, `AccessDecisionServiceTests.AC04_a_repeated_idempotency_key_returns_...` |
| AC05 | `/access-control` connected through `AccessService` | `Demo/AccessDecisionDemoBridgeService.cs` now answers `/api/v1/access/primary-action` ("Create policy") — see "AC05 web wiring" below for why `view-access-events` is deliberately NOT touched |

## Design decisions and scope boundaries

### Integration seam: this feature never re-checks identity/credential status (AC02)

BIO-ACC-001's own `PolicyEvaluationService.EvaluateAsync` already resolves the presented person's
live identity status (via its own `IIdentityStatusGateway`, wrapping BIO-IDN-001's `PersonService`)
and applies deny-precedence for a suspended/revoked identity BEFORE this feature's own
`IPolicyDecisionGateway` ever sees a result. `AccessDecisionService` supplies only
`PersonId`/`CredentialClass`/`LocationId`/`PresentedCardStatus`/the derived
`PresentedAssuranceLevel` and lets BIO-ACC-001's own engine decide — re-deriving or duplicating
that logic here would risk it drifting out of sync with the one real policy engine in this
codebase. `CredentialClass`/`PresentedCardStatus` are themselves caller-supplied evaluation inputs,
not gateway-resolved facts, for the identical reason BIO-ACC-001's own
`PolicyEvaluationContext.PresentedCardStatus` is (no declared dependency on BIO-CRD-005 or a card
system of record).

### AC01: biometric evidence, gathered before the policy is ever consulted

`AccessDecisionService.DecideAsync`'s order is fixed and deliberate: (1) call
`IFingerprintEvidenceGateway`/`IFaceEvidenceGateway` if a sample was supplied (both optional — a
card-only presentation is a legitimate Basic-assurance request, not an error); (2) derive
`AssuranceLevel` from the RAW comparison outcomes via `AccessDecisionCombiner.DeriveAssuranceLevel`
(a single matched modality → Elevated, two independently matched → High, anything else → Basic —
never elevated by a failed/missing attempt); (3) call `IPolicyDecisionGateway.EvaluateAsync` with
that derived value; (4) the gateway's own `Decision`/`ReasonCode` becomes this decision's
`Decision`/`ReasonCode`, unconditionally. Step 4 never looks back at step 1's raw evidence — a
MATCHED fingerprint cannot itself flip a policy DENY to ALLOW, proven directly.

### An unenrolled biometric reference is a routine outcome, not a crash

BIO-BIO-004's/BIO-BIO-005's own `VerifyAsync`/`CompareAsync` throw
`FingerprintVerificationException`/`FaceVerificationException` when the claimed identity has no
authorised reference enrolled at all — a genuine precondition failure on their side, but a
routine, expected situation for THIS feature's own reader/gate caller (an unenrolled person
presenting at a gate is not a bug). `BioAcc002Module.cs`'s own `FingerprintEvidenceGatewayAdapter`/
`FaceEvidenceGatewayAdapter` catch that exception and report
`BiometricComparisonOutcome.NotEnrolled` instead, so the orchestration in `AccessDecisionService`
never has to special-case it — it just flows through `DeriveAssuranceLevel` like any other
non-matching outcome (no elevation, no crash).

### ADR-0006: this feature's own genuine authorization decision

Unlike BIO-ATT-001's/BIO-TRN-002's own capture endpoints (audit-attribution only, ADR-0006
Decision 2), `POST /api/v1/access-decisions/decisions` reaches the SAME genuine authorization
outcome BIO-ACC-001's own gated `/api/v1/access-policies/{policyKey}/evaluate` route reaches, one
hop removed — a caller who could invoke this endpoint without a verified provenance proof could use
it as a side door around BIO-ACC-001's own ADR-0006 Decision 3 gate. `Api/ActorProvenanceGate.cs`
is hand-ported feature-locally, exactly as every prior story's own copy, and required at this
feature's own endpoint layer BEFORE `AccessDecisionService.DecideAsync` ever runs.
`PolicyDecisionGatewayAdapter` also translates BIO-ACC-001's own `AccessPolicyError` (its RBAC
`permission_required` failure, its own validation errors, etc.) into this feature's own
`AccessDecisionError` at the gateway boundary — the foreign exception type must never propagate
past this feature's own endpoint error handler, which only catches its own error type.

### AC05 web wiring: a genuine route collision with BIO-ACC-003, discovered and resolved

`governance/ui-integration-map.json` maps this story to `/access-control` through `AccessService`.
`web/features/access/live.ts` already had two ports: `accessLivePort` ("Create policy",
`/api/v1/access/primary-action`) and `accessViewAccessEventsLivePort` ("View access events",
`/api/v1/access/view-access-events`) — BIO-UX-010's own scaffolding. **While wiring this story's
own AC05, `POST /api/v1/access/view-access-events` was found to already be mapped by BIO-ACC-003
(AccessCache)'s own `AccessCacheEndpoints.cs`** (confirmed independently — `dotnet run` produced a
real `AmbiguousMatchException` when both features attempted to map it). BIO-ACC-003's own
`AccessCacheDemoBridgeService` already answers that route for real, backed by its own
presentation-event log — a genuinely different, already-shipped concern from this story's own
policy-decision pipeline. This story therefore maps **only** `/api/v1/access/primary-action`
("Create policy"), which naturally ties into the SAME policy aggregate this feature's decision
pipeline evaluates against, and does not touch `view-access-events` at all.

`Demo/AccessDecisionDemoBridgeService.CreatePolicyAndComputeDemoDecisionAsync` does two real things
per call: finds-or-publishes a fixed demo `AccessPolicy` through BIO-ACC-001's own real aggregate
(`AccessPolicy.Draft`/`.Publish`, a genuine maker-checker create+publish with two different demo
actor ids — not a stub), then computes and persists one real `AccessDecisionEvent` for a
deterministically-cycled demo person against that published policy. It is registered Singleton
(headerless UI bridge — a bare POST resolves no ambient tenant), and calls BIO-ACC-001's own PURE
`PolicyEvaluator.Evaluate` directly with a fixed `IdentityStatus.Active` rather than routing
through the RBAC/provenance-gated `PolicyEvaluationService` — a headerless demo request has no
provisioned actor to present, the same reason every other demo bridge in this codebase bypasses its
own tenant-scoped service and calls domain statics directly instead (see
`IAccessDecisionDemoSeedProvisioner`'s own header). `web/next.config.mjs` gained one new rewrite
rule for `/api/v1/access/primary-action` (the sibling `view-access-events` rule already existed,
added by BIO-ACC-003). `access-page.tsx`, `ports.ts`, `mock.ts` and `route-manifest.ts`'s
primary/secondary action labels are all untouched — no UI redesign.

### Biometric evidence is not exercised by the demo bridge — a disclosed boundary

BIO-BIO-002 (fingerprint enrolment) and BIO-BIO-003 (face enrolment) are **not declared
dependencies** of this story (only BIO-BIO-004/BIO-BIO-005, the VERIFICATION services, are) — so
the demo bridge has no legitimate way to seed an enrolled biometric reference for its fixed demo
person without taking an undeclared dependency. The demo bridge therefore exercises the OTHER half
of this feature end to end (policy create+publish, evidence-derived assurance computation with an
empty evidence set, real decision persistence). AC01's own evidence-gathering path (matched/
not-matched/acquisition-failed/not-enrolled, and their effect on derived assurance) is proven
directly against the real gateway adapters' logic and fakes in `AccessDecisionServiceTests.cs`/
`AccessDecisionCombinerTests.cs`, and the real cross-story RBAC/provenance wiring is proven through
the real packaged host in `AccessDecisionsHostChannelTests.cs` (which — like
`AccessPolicyHostChannelTests.cs`'s own identical, disclosed limitation — can only demonstrate a
request reaching BIO-ACC-001's real gated service and being rejected for lack of authority, since
this test project has no way to seed a real permission grant through any public endpoint,
BIO-IDN-006-QA-01).

## Testing

- `dotnet test tests/features/bio-acc-002/Biolog.Features.AccessDecisions.Tests.csproj`
- `bash tests/features/bio-acc-002/BIO-ACC-002.test.sh`
- `dotnet test tests/dotnet/Biolog.IntegrationTests/Biolog.IntegrationTests.csproj --filter FullyQualifiedName~AccessDecisionsHostChannelTests`
