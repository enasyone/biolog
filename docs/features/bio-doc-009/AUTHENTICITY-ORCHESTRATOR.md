# BIO-DOC-009 — Automatic authenticity assessment orchestrator

**Story:** As a document-verification operator, I need an explainable authenticity assessment that
combines supported structural, cross-source and cryptographic checks without overclaiming
certainty.

## Acceptance criteria and where each is met

- **AC01** — Assessment reports individual checks and overall policy result.
  `AuthenticityPolicyEngine.BuildChecks` returns exactly six named checks
  (`AuthenticityCheckId`: `MrzStructuralValidity`, `BarcodeDecode`, `OcrFieldExtraction`,
  `DocumentClassification`, `CrossSourceFieldComparison`, `PassiveAuthentication`) grouped as the
  user story names them — structural (the first four), cross-source, and cryptographic.
  `AuthenticityPolicyEngine.DetermineDisposition` computes the single overall
  `AuthenticityDisposition`. `AuthenticityAssessmentResult` carries both `Checks` and
  `PolicyDisposition` together, and the HTTP response (`AuthenticityAssessmentResponse`) surfaces
  both.
- **AC02** — Barcode decode alone can never produce DOCUMENT AUTHENTIC.
  `DetermineDisposition` only ever consults `CrossSourceFieldComparison` and
  `PassiveAuthentication` to decide `DocumentAuthentic` — `BarcodeDecode`'s own outcome is never
  read there at all, by construction. This is sufficient because `CrossSourceFieldComparison` can
  only reach `Passed` when at least one field has two or more independent sources agree
  (BIO-DOC-008's own `FieldMatchStatus.Unknown` rule: "fewer than two sources ... never silently
  reported as a match"), so a barcode-decode-only input — nothing else to compare against — can
  never make it there. See `AuthenticityPolicyEngineTests`'s `AC02_*` tests for the executable
  proof, including a direct test that flips `BarcodeDecode` between `Passed`/`Failed` while holding
  every other check fixed and shows `DetermineDisposition` never reads it for the promotion rule.
- **AC03** — Unsupported checks are marked not performed, not passed.
  `AuthenticityCheckOutcome.NotPerformed` is a distinct value, ordered first so it is the enum's
  own safe default (`default(AuthenticityCheckOutcome)`). Every `AuthenticityAssessmentInputs`
  field defaults to `NotPerformed`; every `BuildXxxCheck` helper in `AuthenticityPolicyEngine`
  defaults to `NotPerformed` for an absent/unattempted input and never infers `Passed` from the
  mere absence of a failure. The disposition policy also never defaults to `DocumentAuthentic` for
  lack of a `Failed` check — an input where every check is `NotPerformed` reaches `Inconclusive`.
- **AC04** — Evidence, template and ruleset versions are recorded.
  `AuthenticityAssessmentResult.EvidenceVersion` is BIO-DOC-008's own already-controlled
  `CrossComparisonResult.VersionNumber` (this story mints no separate version sequence of its own —
  reusing 008's controlled sequence avoids a second, possibly-inconsistent counter for the same
  underlying evidence). `TemplateId`/`TemplateVersionNumber` come from the winning classification
  candidate's own `DocumentIntelligenceRegistryEntry` (BIO-DOC-006), null when classification was
  not performed or found no match. `RulesetVersion` is `AuthenticityPolicyEngine.RulesetVersion`
  ("authenticity-policy-v1"), recorded verbatim so a later audit can tell which POLICY, not just
  which evidence, produced a given disposition.
- **AC05** — Manual review can override workflow disposition but cannot rewrite raw evidence.
  `AuthenticityManualOverride` is a layer ON TOP of the immutable `AuthenticityAssessmentResult`,
  never a mutation of it — the same two-tier shape BIO-DOC-001's own `DocumentConflict`/
  `ManualReviewState` established for this epic. `AuthenticityAssessment.ApplyManualOverride`
  never assigns to `Result` at all; the only field it writes is `Override`, a wholly separate
  reference. `Result`'s own `AuthenticityCheckResult` entries are themselves `internal init`-locked
  records with no public mutator anywhere. `EffectiveDisposition` (`Override?.Disposition ??
  Result.PolicyDisposition`) is the one place the override changes downstream behaviour;
  `Result.PolicyDisposition` stays exactly what the policy engine computed, permanently, for
  audit/dispute purposes. `AuthenticityAssessmentTests`'s
  `AC05_a_manual_override_never_changes_the_individual_checks_the_raw_evidence` proves this with a
  same-OBJECT-REFERENCE assertion (`Assert.Same`), not just equal-looking values. CAS-versioned
  (`AuthenticityAssessmentConcurrencyException`) the same optimistic-concurrency discipline ADR-0001
  Decision 3 establishes elsewhere; the override itself is durably audited before returning
  (`authenticity_assessment.manually_overridden`). AC05 names the CAPABILITY, not an HTTP surface —
  see "The read and override capability" below for why it is proven at the domain/service layer
  only, not reachable over HTTP yet (Zainab's BIO-DOC-009-F01 security review).
- **AC06** — The approved web surface (`/document-verify`) is connected through
  `DocumentVerifyService` to a real live implementation, and the mapped end-to-end human workflow
  passes without an unapproved UI redesign. See "Proving AC06" below — `DocumentVerifyApp.tsx` and
  `document-verify.module.css` (BIO-UX-009's own, already-reviewed files) are untouched by this
  story.

## Why this project's own declared dependencies are BIO-DOC-006 and BIO-DOC-008 only

The story's own DEPENDENCIES line names BIO-DOC-006 and BIO-DOC-008 — not BIO-DOC-002/003/004/005/007.
`Biolog.Features.AuthenticityAssessment.csproj` takes exactly two `ProjectReference`s, matching that
line exactly (`scripts/check-module-project-references.sh`-adjacent discipline this epic enforces
by convention). Consequences, each deliberate:

- MRZ/barcode/OCR structural-check outcomes are taken as plain `AuthenticityCheckOutcome`+detail
  pairs on `AuthenticityAssessmentInputs`, not the concrete BIO-DOC-002/003/004 result types — the
  same "plain domain parameter, not a resolved dependency" pattern BIO-DOC-008 itself already
  established for its own undeclared OCR dependency (BIO-DOC-004).
- The cryptographic (passive authentication) signal is taken exclusively through BIO-DOC-008's own
  `CrossComparisonResult.NfcCorroboration` — already folded in from BIO-DOC-007 by that story —
  rather than a second, direct BIO-DOC-007 reference of this story's own.
- A real consequence, documented rather than hidden: this project cannot itself construct a
  `CrossComparisonResult` whose `NfcCorroboration.Available` is `true` (that requires a real
  BIO-DOC-007 `ChipReadOutcome`), nor a `DocumentClassificationOutcome` with a non-empty candidate
  list (that requires a real BIO-DOC-004 `TemplateOcrResult`). `AuthenticityPolicyEngineTests`
  exercises those specific branches by testing `DetermineDisposition` directly against hand-built
  `AuthenticityCheckResult` lists instead (enabled by this project's own `InternalsVisibleTo` grant
  to its own test assembly — a fully self-contained, zero-cross-story-coupling test seam). The
  input-to-check TRANSLATION for those two branches is a one-line construction each in
  `BuildClassificationCheck`/`BuildPassiveAuthenticationCheck`, provably correct by inspection; the
  POLICY decision those checks feed into (`DetermineDisposition`) — what AC01/AC02/AC03 actually
  govern — is fully covered either way.
- The three synthetic HTTP demo fixtures (`SyntheticAuthenticityFixtures.cs`) leave classification
  and passive authentication unset for the same reason. `FullyVerified` still genuinely reaches
  `DocumentAuthentic` on cross-source field agreement alone (two independently-supplied fields
  agreeing across MRZ/barcode/OCR sources) — real, reachable evidence within this story's own
  boundary, not a shortcut.

## No real capture/upload pipeline exists yet — the HTTP demo's own shape

`/document-verify` (BIO-UX-009) never uploads a real document image or collects a real document
session id — every step is a simulated port call by design (BIO-UX-009-AC04's own security
requirement, applied uniformly). `web/features/documents/live.ts`'s `checkAuthenticity` therefore
`POST`s `/api/v1/documents/authenticity` with **no request body and no session id at all** — a
contract already reviewed and merged before this story existed, which this story does not change
(AC06 forbids an unapproved UI redesign).

`AuthenticityAssessmentService` resolves this by cycling deterministically through three named
synthetic document sessions (`demo-session-fully-verified`, `demo-session-barcode-only`,
`demo-session-conflict-detected`) by a single shared call counter — the same
deterministic-cycle mechanism `web/lib/module-port.ts`'s own mock adapter and this story's own
frontend sibling (`evidence.ts`'s `runEvidenceScenario`) already use. Each call runs the REAL
`AuthenticityPolicyEngine`/`AuthenticityAssessmentResult.Compute` against one of the three fixtures
— a genuinely computed, non-canned result every time, not a fake always-succeeds demo (mirroring
BIO-UX-009's own "capture's first call is `failed`, not a rigged demo" precedent).

| Fixture | Structural checks | Cross-source | Disposition |
|---|---|---|---|
| `FullyVerified` | MRZ/Barcode/OCR all Passed | 2 fields, 2 agreeing sources each → Passed | `DocumentAuthentic` |
| `BarcodeOnly` | Barcode Passed, MRZ/OCR NotPerformed | 1 field, 1 source only → NotPerformed (AC02's own scenario, live) | `Inconclusive` |
| `ConflictDetected` | MRZ/OCR Passed, Barcode NotPerformed | 1 field, 2 DISAGREEING sources → Failed | `DocumentNotAuthentic` |

## The read and override capability — deliberately NOT an HTTP surface (Zainab's BIO-DOC-009-F01)

An earlier version of this story mapped `GET /api/v1/documents/authenticity/{documentSessionId}`
and `POST .../{documentSessionId}/override` as real HTTP routes. Zainab's security review (High)
found both genuinely exploitable: the GET returned a full assessment — including individual
evidence/check results — to any caller who knew or guessed a session id, with no actor/permission/
tenant check at all; the override route only required a nonblank, caller-supplied `X-Actor-Id`
header, with no RBAC or trusted-actor provenance, so any network caller could durably rewrite a
disposition and have the forged identity recorded in the audit trail as if it were real.

No trusted-actor-provenance/RBAC/tenant-context boundary exists anywhere in this codebase yet
(BIO-INT-001 is W12) — the same disclosed seam `ActorResolver`'s own remarks already named for the
compute route — but that disclosure does not make an exposed, consequential HTTP surface safe.
Her review offered two remediations: build the real authorization boundary, or keep the endpoints
inaccessible until one exists. Building RBAC/tenant-context binding ad hoc for one story would
itself be a shared-authorization-contract change this story has no standing to make unilaterally,
and neither route is actually required: AC06 only names the compute route (the one the approved UI
calls); AC05 requires the override *capability* to exist and be evidenced, never that it be
reachable over HTTP.

**Fix:** `GetAsync`/`OverrideAsync` and their route mappings were removed entirely from
`AuthenticityEndpoints.cs` (not just left unmapped) — there is no dead, secured-away HTTP handler
to bit-rot or be re-enabled by accident. `AuthenticityAssessmentService.Get`/`ApplyOverride` (the
service-layer capability underneath) were kept as the tested seam a later story — one that actually
builds the authorization boundary — maps real, authorized endpoints onto, the same "seam kept, only
the endpoint exposure deferred" shape `DocumentModule` (BIO-DOC-001) already established for
`IDocumentSessionStore`. Since these two service methods lost their only caller (the removed HTTP
handlers), a dedicated `AuthenticityAssessmentServiceTests.cs` now exercises them directly — which
caught a real, independent bug: `SyntheticAuthenticityFixtures` had owned a single process-wide
*static* `IVersionStore`, which desynchronised from a caller's own per-session version bookkeeping
the moment more than one independent `AuthenticityAssessmentService` instance (e.g. one per test)
computed for the same well-known demo session id. Fixed by having the fixtures take the version
store/audit sink as parameters instead, so each service instance owns consistent, isolated state.

## Genuine HYBRID mode, not all-or-nothing (AC06)

`DocumentVerifyService`'s `MODULE_PORT_KEYS` entry grows a second port key, `authenticity`,
independent of `primaryAction` — the same mechanism `ReportingService` already established
(`feature-mode.ts`'s own `MODULE_PORT_KEYS` comment). `NEXT_PUBLIC_FEATURE_MODE_DOCUMENTVERIFYSERVICE_AUTHENTICITY=LIVE`
connects ONLY the authenticity step; the other seven Document Verify steps (capture, MRZ, barcode,
OCR, document identification, NFC, selfie comparison) stay SIMULATION-only — no backend exists for
them yet — so the module genuinely reaches `HYBRID` (`getModuleStatus('DocumentVerifyService')`),
not an all-or-nothing switch. `web/features/documents/mock.ts`/`live.ts` each now export a second,
fully independent port instance (`documentsAuthenticityMockPort`/`documentsAuthenticityLivePort`)
with their own call counters, mirroring `web/features/reporting/mock.ts`'s own two-instance shape.
`web/features/documents/index.ts` composes the two resolved ports into the single `documentsPort`
object `DocumentVerifyApp.tsx` already calls every step through — every method resolves from the
`primaryAction` key except `checkAuthenticity`, which always resolves from the independent
`authenticity` key regardless of the module's whole-module default. `DocumentVerifyApp.tsx` itself
needed no change at all.

## The Next.js rewrite (why AC06 needed one)

`fetch('/api/v1/documents/authenticity')` resolves against the Next.js server's OWN origin with no
rewrite, not the separate `Biolog.Api` (.NET) host — 404, even once a real backend exists.
`web/next.config.mjs` gained a `rewrites()` entry forwarding `/api/v1/documents/*` server-side to
`Biolog.Api` (`BIOLOG_API_BASE_URL`, defaulting to `Biolog.Api`'s own documented dev port, 5080) —
scoped narrowly to this story's own owned route family, no other module's `/api/v1/*` path is
touched, and no CORS configuration is needed on either side since the browser only ever sees one
origin.

## Proving AC06 (manual verification, actively excluded from the default full-suite run)

Every other BIO-UX-009 Playwright spec runs against a SIMULATION-only Next.js server — no `.NET`
host needed. `bio-doc-009-authenticity-live.spec.ts` is the one spec in this repository that needs
BOTH servers running together. It carries its own `test.skip(!process.env.BIOLOG_DOC009_LIVE_TESTS,
...)` guard — a documentation-only exclusion was tried first and genuinely failed
`scripts/quality.sh --full`'s own default `pnpm --dir tests/frontend test:e2e` run (that suite's
server never sets the LIVE env var, so the spec's real-backend assertions failed against mock data
instead of skipping); the guard fixes that for real, not just on paper.

```bash
# Terminal 1 — the real .NET backend
dotnet run --project api/Biolog.Api/Biolog.Api.csproj --urls http://127.0.0.1:5080

# Terminal 2 — a production Next.js build with authenticity's LIVE mode baked in
# (NEXT_PUBLIC_* vars are inlined at BUILD time, not read at runtime)
cd web
rm -rf .next
NEXT_PUBLIC_FEATURE_MODE_DOCUMENTVERIFYSERVICE_AUTHENTICITY=LIVE BIOLOG_API_BASE_URL=http://127.0.0.1:5080 pnpm build
BIOLOG_API_BASE_URL=http://127.0.0.1:5080 pnpm start -p 3014

# Terminal 3 — the spec itself, single worker (see the spec file's own header comment for why),
# with BIOLOG_DOC009_LIVE_TESTS=1 to lift the test.skip guard
cd tests/frontend
BIOLOG_BASE_URL=http://127.0.0.1:3014 BIOLOG_DOC009_LIVE_TESTS=1 ./node_modules/.bin/playwright test bio-doc-009-authenticity-live.spec.ts --workers=1
```

## Tests

- `tests/features/bio-doc-009/Biolog.Features.AuthenticityAssessment.Tests/AuthenticityPolicyEngineTests.cs`
  — AC01 (all six checks reported, non-blank detail, one overall disposition), AC02 (barcode-alone
  never Authentic, including the direct "outcome never read" proof), AC03 (every unattempted check
  is `NotPerformed`, disposition never defaults to `DocumentAuthentic`), plus the general
  disposition rules (a mismatch forces `DocumentNotAuthentic`, two-source agreement reaches
  `DocumentAuthentic`).
- `AuthenticityAssessmentResultTests.cs` — AC04 (evidence/template/ruleset versions), and the
  audit-before-commit discipline mirrored from BIO-DOC-008's own `CrossComparisonResult.Compute`
  (durable write before return, ambiguous-write resolution via `ExistsAsync`, strict RFC 3339
  timestamp validation, blank actor/correlation-id rejection).
- `AuthenticityAssessmentTests.cs` — AC05 (override changes the effective disposition, never the
  raw checks — same-reference proof; CAS version conflicts rejected; the override is itself
  durably audited; `ReplaceResult`/`ClearOverride`'s own independent behaviour).
- `AuthenticityAssessmentResultTypesAreLockedDownTests.cs` — standing reflection-based lockdown
  regression test, the same discipline every prior BIO-DOC-00x result type in this epic carries.
- `AuthenticityAssessmentServiceTests.cs` — direct, non-HTTP coverage of `Get`/`ApplyOverride`
  (the seam left behind after Zainab's BIO-DOC-009-F01 security review removed their HTTP routes —
  see "The read and override capability" above) and `ComputeNextDemoAssessment`'s own deterministic
  cycling/version-advancement. Caught a real bug: `SyntheticAuthenticityFixtures`'s own static
  version store desynchronising across independent service instances — fixed by parameterizing it.
- `tests/frontend/unit/bio-doc-009-documents-authenticity-port.test.ts` — the new independent
  `authenticity` port key resolves its own distinct instance, starts in mock mode, and can switch
  to LIVE without affecting `primaryAction`'s own mode.
- `tests/frontend/playwright/bio-doc-009-authenticity-live.spec.ts` — the real end-to-end AC06
  proof (see "Proving AC06" above): three consecutive live calls cover all three real computed
  dispositions, the raw HTTP response carries genuine AC04 version fields (not just a bare state
  string), the evidence-comparison table reflects the live result the same as any simulated step,
  and zero axe accessibility violations once a live result is shown.

## Security/privacy

Every synthetic fixture value (document numbers, dates of birth) is fabricated, not real PII — the
same SYNTHETIC discipline this entire epic follows. The compute endpoint accepts no file
upload/camera stream and stores no data beyond the three fixed, in-memory demo sessions. The
override endpoint requires an asserted actor and records who/why, matching AC05's own auditability
requirement.
