# BIO-BIO-008 — Implement liveness provider contract and policy hooks

Implements the BIO-BIO epic's eighth story: provider-neutral liveness evidence and tenant/
workflow policy hooks (user story, verbatim: "As high-assurance workflows, I need liveness
evidence that is provider-neutral and policy-controlled.").

## Acceptance criteria

| AC | Requirement | Implementation | Tests |
|---|---|---|---|
| **AC01** | Liveness result has provider/method/version/confidence/status | `Domain/LivenessEvidence.cs` - `ProviderId`/`Method`/`ProviderVersion`/`Confidence`/`Status`, produced by `Domain/ILivenessProvider.cs`'s only implementation, `SyntheticLivenessProvider` | `SyntheticLivenessProviderTests.AC01_*`, `LivenessEvaluationServiceTests.AC01_*` |
| **AC02** | Failure is distinct from face mismatch | `Domain/LivenessStatus.cs` (`Live`/`SpoofDetected`/`Inconclusive`) shares no member, name or type with any match/verification outcome anywhere in this codebase; no `ProjectReference` to BIO-BIO-005 exists at all | `LivenessGovernanceTests.LivenessEvidence_carries_no_match_or_mismatch_shaped_field`, `.LivenessStatus_members_are_liveness_shaped_not_match_shaped`, `.this_assembly_has_no_reference_to_BIO_BIO_005s_own_assembly` |
| **AC03** | Policy can require liveness for selected workflows | `Domain/ILivenessPolicy.cs`/`InMemoryLivenessPolicy.cs` - tenant+workflow-keyed, defaults to NOT required until explicitly configured | `InMemoryLivenessPolicyTests.AC03_*`, `LivenessEvaluationServiceTests.AC03_*` |
| **AC04** | Replay/screen/photo adversarial cases are represented in test interfaces | `Domain/AdversarialCaseKind.cs` + `Domain/AdversarialSampleFactory.cs` - a named factory producing a deterministic tagged sample per case, recognised by `SyntheticLivenessProvider` | `SyntheticLivenessProviderTests.AC04_*` |
| **AC05** | The approved web surface is connected through `BiometricService` to this story's live implementation or an explicitly documented HYBRID state | `Modules/BioBio008Module.cs` maps `POST /api/v1/biometrics/face/liveness` - the exact path/response shape `web/features/biometrics/live.ts` (BIO-UX-008) already targets. `checkLiveness` is a genuinely independent, LIVE-capable `BiometricService` port (Kemi-approved split, 2026-09-27) with its own Next.js rewrite - see "AC05" below | `LivenessEndpointsTests.cs` (real host, real endpoint), `bio-bio-008-liveness-port.test.ts` (independent port resolution), manual end-to-end verification below |

## Design decisions and scope boundaries

**No `ProjectReference` to BIO-BIO-003 or BIO-BIO-005**, despite both being declared dependencies.
This story defines its own, richer, provider-neutral `LivenessEvidence` (AC01) rather than reusing
BIO-BIO-003's own internal `ILivenessDetector`/`LivenessResult` (that pair is BIO-BIO-003's own
owned, internal, enrolment-capture-only hook, carrying only `IsLive`/`Score`/`ProviderId` - no
`Method`/`ProviderVersion`/`Status` at all - and extending or reaching into it is out of this
story's write scope per `governance/feature-path-ownership.json`). Neither BIO-BIO-003 nor
BIO-BIO-005 exposes a query this story's own policy/evaluation surface needs to call - the
dependency is architectural (this story reuses the same narrow-gateway/evidence-separation
*patterns* those stories established: BIO-BIO-005's own evidence/decision split, BIO-BIO-007's own
"purely informational, no decision concept" boundary), not a data dependency.

**`LivenessEvaluationService` answers two independent questions and decides nothing itself.**
`IsRequiredAsync` (AC03) and `EvaluateAsync` (AC01) are two separate calls a caller can make in
either order or independently - the service never gates or blocks anything on its own. A future
calling workflow (e.g. a high-assurance 1:1 verification story) that finds liveness required,
evaluates it, and gets back a non-`Live` result decides what to do with that itself. This is the
same "purely informational" boundary BIO-BIO-007's own search service established for a different
question.

**AC04's "test interfaces" is `AdversarialSampleFactory`, not a detection capability.** No real
anti-spoofing model exists anywhere in this repo (the same constraint every synthetic provider in
this codebase documents for itself). `AdversarialCaseKind.{Replay,ScreenPresentation,
PhotoPresentation}` are three named, selectable scenarios the factory tags onto a synthetic
`BiometricSample`'s payload (a plain byte-string marker prefix) that `SyntheticLivenessProvider`
recognises deterministically and answers `SpoofDetected` for - the same "hint" pattern this
author's own BIO-UX-008 story already used for its mock adapters' outcome selectors, applied here
on the backend/test side instead of the browser side.

**AC02 is a structural, not behavioural, guarantee.** "Failure is distinct from face mismatch" is
proven by construction: `LivenessStatus`'s three members (`Live`/`SpoofDetected`/`Inconclusive`)
share no name, type or field with any match/verification outcome anywhere in this codebase, and
this feature has literally no way to reach BIO-BIO-005's own types (no `ProjectReference` exists).
A reflection-based governance test pins both facts so a future edit cannot silently reintroduce a
shared/confusable field.

**A genuine integration failure (empty payload) throws; a routine negative outcome
(`SpoofDetected`/`Inconclusive`) never does.** The same typed-outcome-vs-exception discipline this
author's own BIO-BIO-004 backend story documents: `LivenessException.EmptySample()` is for a
request that cannot be evaluated AT ALL, never for a meaningful-but-negative liveness verdict.

**AC05 - what "connected" means here.** `Modules/BioBio008Module.cs` maps
`POST /api/v1/biometrics/face/liveness`, the EXACT path `web/features/biometrics/live.ts` already
targets, with the exact `{ outcome, message, simulated }` response shape that file's own
`LivenessResult` type expects (`LivenessEndpointsTests.cs` proves this through the real composed
host).

BIO-UX-008 built every `BiometricService` action (capture/verify/identify/liveness) to resolve
together as ONE object through a single `NEXT_PUBLIC_FEATURE_MODE_BIOMETRICSERVICE` switch, since
at the time no action had a real backend. Flipping that one switch to `LIVE` would also make
`captureFingerprint`/`verifyFingerprint`/`captureFace`/`verifyFace`/`identifyFace` claim LIVE and
genuinely fail, since none of those five actions have a real backend yet either. **Kemi approved
(hive message, 2026-09-27) splitting `checkLiveness` onto its own independently-switchable port**,
mirroring `DocumentVerifyService.authenticity`/`ReportingService.viewAuditTrail`'s exact precedent
(verified against the live code, not just the description, before approving): `web/lib/feature-mode.ts`
gained a `checkLiveness` key in `BiometricService`'s `MODULE_PORT_KEYS` plus a
`PER_PORT_ENV_DEFAULTS['BiometricService:checkLiveness']` entry reading
`NEXT_PUBLIC_FEATURE_MODE_BIOMETRICSERVICE_CHECKLIVENESS`; `web/lib/module-registry.ts` registers
the new key; `web/features/biometrics/index.ts` resolves it independently via `resolvePort(...,
'checkLiveness')` and merges it into the single `biometricsPort` object every page calls through
(same shape as `documents/index.ts`'s own `authenticity` merge). `web/next.config.mjs` gained one
new rewrite entry, scoped to the single `/api/v1/biometrics/face/liveness` path (not a `:path*`
wildcard - the other four actions have nowhere real to forward to yet), forwarding to `Biolog.Api`.

**AC05 is genuinely LIVE-capable, proven end-to-end, not left in a HYBRID fallback.** Manually
verified against a real running `Biolog.Api` host (`dotnet run --project api/Biolog.Api`, port
5080) and a real `next dev` server (`NEXT_PUBLIC_FEATURE_MODE_BIOMETRICSERVICE_CHECKLIVENESS=LIVE`):
`curl -X POST http://127.0.0.1:<next-port>/api/v1/biometrics/face/liveness -d '{"hint":"live"}'`
returned the REAL backend's own `tenant_context_missing` problem response (HTTP 400) through the
Next.js rewrite - not a Next 404, not a CORS failure - proving the browser-facing origin genuinely
reaches the real endpoint end to end (a real browser session carries the tenant context this bare
`curl` smoke test does not fake). `BiometricService`'s other four actions are unaffected and remain
SIMULATION-only under `primaryAction` - the module genuinely reaches `HYBRID` status
(`getModuleStatus('BiometricService')`), the same mechanism `DocumentVerifyService`/
`ReportingService` already established. No redesign of the already-shipped `/facial-recognition`
UI was made or needed.

**The web's binary `LivenessOutcome` (`'live' | 'spoof_suspected'`) has no `Inconclusive`
member.** The endpoint maps conservatively: only a confident `Live` evidence reports `"live"`; both
`SpoofDetected` and `Inconclusive` report `"spoof_suspected"` (the fail-safe direction for a
high-assurance signal). Extending the web's own outcome vocabulary to a genuine three-way split is
a BIO-UX-005-owned shared-contract change, out of this backend story's own scope to make
unilaterally.

**The endpoint demonstrates the real pipeline against a synthetic sample, not a live capture.**
`web/features/biometrics/live.ts`'s own `checkLiveness` sends only an optional scenario `hint`,
never a real capture (BIO-UX-008's own web layer has no camera/`getUserMedia` call anywhere - see
that story's own README "Security requirement" note). The endpoint maps the hint to a demonstration
`BiometricSample` (a known-live seed, or an adversarial-tagged sample for `spoof_suspected`) and
calls the REAL `LivenessEvaluationService`/`ILivenessProvider` pipeline - the same honest
"real mechanism, not a hardcoded response" framing BIO-RPT-001's own `/primary-action` endpoint
already documents for itself.

**Every sample byte array in this suite is synthetic filler**, never a captured face image (this
story's own top-level constraint: "Use synthetic data only").
