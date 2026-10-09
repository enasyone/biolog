# BIO-BIO-005 — Implement face 1:1 verification

Implements the BIO-BIO epic's fifth story: face comparison between a live/current capture and the
claimed person's approved reference, built on BIO-BIO-003's own quality/liveness contracts and
BIO-BIO-001's own provider-neutral biometric contracts (user story, verbatim: "As a verification
workflow, I need face comparison between live/current capture and an approved reference for the
claimed person.").

## Acceptance criteria

| AC | Requirement | Implementation | Tests |
|---|---|---|---|
| **AC01** | Similarity, threshold policy, quality and model version are recorded | `Domain/FaceVerificationEvidence.cs` (`Score`, `ThresholdUsed`, `QualityFaceCount`/`QualityApproved`, `ProviderId`/`ProviderVersion`) is persisted via `Domain/IFaceVerificationEvidenceStore.cs` on every `CompareAsync` call, regardless of outcome | `FaceVerificationServiceTests.AC01_*` |
| **AC02** | Threshold is policy-based rather than arbitrary UI input for ordinary users | `Domain/IFaceVerificationThresholdPolicy.cs`; `FaceVerificationService.CompareAsync` has no threshold parameter in its own signature at all - the threshold used is always `thresholdPolicy.GetThreshold(tenantId)` | `FaceVerificationServiceTests.AC02_*` |
| **AC03** | Verification evidence is separate from authorization decision | `Domain/FaceVerificationEvidence.cs` carries no allow/deny field at all; `Domain/FaceAuthorizationPolicy.Decide` is a separate, pure, static function that derives a `Domain/FaceAuthorizationDecision.cs` FROM a piece of evidence - `CompareAsync` never computes one | `FaceVerificationServiceTests.AC03_*` |
| **AC04** | Reference/photo lifecycle changes are handled deterministically | `Domain/IFaceReferenceGateway.GetLatestApprovedReferenceAsync` is resolved fresh on every call (never cached), so a re-capture that supersedes the approved reference is picked up automatically; no reference at all is a precondition failure (`FaceVerificationException.NoAuthorisedReference`); a reference whose `SourcePhotoMasterVersion` is no longer the claimed identity's CURRENT photo master version (`IFaceReferenceGateway.GetCurrentPhotoMasterVersionAsync`) is a defined, deterministic `FaceComparisonOutcome.ReferenceStale` outcome, checked before any comparison is attempted | `FaceVerificationServiceTests.AC04_*`, `FaceVerificationPhotoLifecycleTests.*` |

## Design decisions and scope boundaries

**Two feature-to-feature `ProjectReference`s, for two different reasons.** BIO-BIO-001 is
referenced directly, the same "plain, host-agnostic contracts library" shape every prior BIO-BIO
story already established. BIO-BIO-003 (this story's own declared dependency) is ALSO referenced
directly, but only for its genuinely public, host-agnostic quality/liveness contracts
(`IFaceQualityChecker`/`FaceQualityAssessment`, `ILivenessDetector`/`LivenessResult`) - the same
"plain interface + synthetic implementation" shape BIO-BIO-001's own provider contracts have, just
declared inside BIO-BIO-003's project. BIO-BIO-003's own `FaceEnrolmentService`/
`IFaceBiometricReferenceRepository` stay unreachable regardless of the reference (both are
`internal`, granted only to `Biolog.Api` and BIO-BIO-003's own test project via
`InternalsVisibleTo`) - resolving "the claimed identity's latest approved reference" (AC04) still
goes through this story's own narrow `Domain/IFaceReferenceGateway.cs`, wired in
`Modules/BioBio005Module.cs` to a real adapter wrapping BIO-BIO-003's already-registered
`FaceEnrolmentService.GetLatestReferenceAsync` - the same "narrow gateway + host-level adapter"
shape every prior BIO-BIO story uses for its own tenant-scoped upstream dependency.

**AC03's separation is architectural, not just two fields on one record.** `FaceAuthorizationPolicy.Decide`
is a static, dependency-free, pure function - deliberately not a method on
`FaceVerificationService` and deliberately taking no I/O of its own. Producing evidence (a
biometric fact, persisted) and deciding authorization (a policy judgement about that fact, never
persisted) are two operations a caller can invoke independently, at different times, under
different policy versions, without ever re-running a biometric comparison. Today's decision rule
is intentionally simple (`Matched` is the only comparison outcome that can produce `Allow`)
because none of this story's four ACs ask for anything richer (e.g. an identity-status gate the
way BIO-BIO-004-AC04 does for fingerprints); a later story that wants a richer policy changes only
`FaceAuthorizationPolicy`, never `FaceVerificationEvidence` or `CompareAsync`.

**AC02's threshold policy is tenant-keyed, never request-keyed.** `IFaceVerificationThresholdPolicy.GetThreshold(tenantId)`
takes no per-request input; `SyntheticFaceVerificationThresholdPolicy.SetThresholdForTenant` is a
configuration-time operation `CompareAsync` never calls itself - the same "ordinary caller has no
path to influence this value" property AC02 asks for, regardless of how many tenants configure
their own override. `SyntheticBiometricMatcher` (BIO-BIO-001) only ever scores exactly 0.0 or 1.0
(exact-byte equality), so this story's own tests use a small `FakeBiometricMatcher` test double
with a continuous score to prove the threshold genuinely drives the outcome at a boundary the
synthetic matcher's binary scoring cannot exercise on its own.

**AC04's determinism rule covers BOTH halves of "reference/photo lifecycle" (BIO-BIO-005-QA-01,
Chioma acceptance, High).** The first builder round only handled reference supersession (a
re-capture) and explicitly called source-photo supersession out of scope, reasoning that
BIO-BIO-003 itself never revalidates a reference's `SourcePhotoMasterVersion` after capture
either. An acceptance probe correctly rejected that as an incomplete reading of AC04: the AC's own
wording is "reference/PHOTO lifecycle changes," not just reference lifecycle, and BIO-PHO-001
allows a new photo to be appended (superseding the old one) without ever forcing a face
re-capture - so a reference can go stale silently, with the service still returning `Matched`
against a live capture compared to a photo that is no longer current. `IFaceReferenceGateway.GetCurrentPhotoMasterVersionAsync`
closes this: `FaceVerificationService.CompareAsync` now compares its result against the resolved
reference's own `SourcePhotoMasterVersion` BEFORE attempting any extraction/comparison, and a
mismatch (or no current photo master at all) records `FaceComparisonOutcome.ReferenceStale` - a
defined, deterministic, always-recorded reject outcome, never a silent match and never an
unhandled ambiguity. `FaceVerificationPhotoLifecycleTests.cs` reproduces the exact acceptance
scenario (photo v1 reference, current photo v2) plus the "no current photo at all" and
"staleness checked before extraction" edge cases.

**Provider/vendor extraction failures are contained from the start (BIO-BIO-004-F01's lesson,
applied proactively).** BIO-BIO-004's own round-1 review found that catching only
`ArgumentException` around a provider's `ExtractTemplateAsync` call let other provider/vendor
failures (a device timeout, a vendor-specific exception, an internal provider-side cancellation)
escape uncaught with no record ever written. This story's `FaceVerificationService.CompareAsync`
catches every exception `IFaceProvider.ExtractTemplateAsync` can raise and records
`FaceComparisonOutcome.AcquisitionFailed`, except a genuine CALLER cancellation (the passed
`CancellationToken` itself was cancelled), which still propagates since nothing about that attempt
was ever decided - the identical carve-out BIO-BIO-004's fix applies, built in from the first
commit rather than discovered by an independent reviewer a second time.

**Live-capture quality is always recorded, never a blocking gate.** AC01 says "quality ... are
recorded," not "poor quality blocks verification" - `IFaceQualityChecker.AssessAsync` always runs
and its `FaceCount`/`IsApproved` summary is always attached to the evidence record, whether or not
extraction/comparison went on to succeed, but a poor-quality live capture does not, by itself,
short-circuit the comparison the way BIO-BIO-003's own capture-time quality gate does for
enrolment. Liveness is likewise not re-checked at verification time - that is BIO-BIO-003's own
AC05 concern for the REFERENCE itself at capture time, not something any of this story's four ACs
ask this story to repeat on every verification attempt.

**No HTTP endpoint is mapped by this story**, the same judgment call every prior BIO-BIO story
made: AC01-AC04 are about the verification/evidence/decision model, not an HTTP surface, and the
service takes a caller-resolved tenant/person id rather than resolving one itself.

**Identifiers are plain strings**, the same explicitly documented choice every prior BIO-BIO story
made; no `identifier-registry.json` prefix is registered for verification evidence (frozen,
ADR-governed contract, out of this story's `read_only_shared_contracts` scope).

**Every sample byte array in this suite is synthetic filler**, never a captured face image (this
story's own top-level constraint: "Use synthetic data only").
