# BIO-BIO-003 — Face capture and quality assessment

Implements the BIO-BIO epic's third story: quality-gated face capture with liveness support,
built on BIO-BIO-001's provider-neutral biometric contracts and linked to BIO-PHO-001's approved
photo master (user story, verbatim: "As enrolment staff/mobile users, I need face capture
quality gates before a biometric reference is created.").

## Acceptance criteria

| AC | Requirement | Implementation | Tests |
|---|---|---|---|
| **AC01** | Exactly one face is required | `Domain/FaceQualityAssessment.FaceCount`/`FaceCountApproved` - a precondition distinct from the seven AC02 checks, checked first | `FaceQualityAssessmentTests.AC01_*` |
| **AC02** | Pose, eyes, lighting, sharpness, resolution, occlusion and background checks are represented | `Domain/FaceQualityCheck.cs` (exactly these seven, a closed enum); `Quality/IFaceQualityChecker.cs`/`SyntheticFaceQualityChecker.cs` assess every capture against all seven | `FaceQualityAssessmentTests.AC02_*`, `SyntheticFaceQualityCheckerTests` |
| **AC03** | Approved reference retains model/version and source-photo linkage | `Domain/FaceBiometricReference.cs`: `Template` reuses BIO-BIO-001's own `BiometricTemplate.ProviderId`/`ProviderVersion` directly (model/version); `SourcePhotoMasterVersion` + `PersonId` is the source-photo linkage, verified to exist via `Domain/IPhotoMasterExistenceGateway.cs` before any capture is attempted | `FaceEnrolmentServiceTests.AC03_*` |
| **AC04** | Poor capture yields actionable recapture guidance | `Service/FaceCaptureOutcome.cs` is a typed result (`Approve`/`Reject`), never an exception, for a failed quality/liveness gate; `FaceQualityCheckResult.Fail` cannot be constructed without a guidance string; `FaceQualityAssessment.RecaptureGuidance` collects every failure reason | `FaceEnrolmentServiceTests.AC04_*` |
| **AC05** | Liveness hook is supported without hard-coding a vendor | `Liveness/ILivenessDetector.cs` is the only seam `FaceEnrolmentService` reaches for a liveness verdict; `SyntheticLivenessDetector` is the only shipped implementation, and participates in the approval gate alongside the seven quality checks | `FaceEnrolmentServiceTests.AC05_*`, `SyntheticLivenessDetectorTests` |

## Design decisions and scope boundaries

**Built directly on BIO-BIO-001**, the same feature-to-feature `ProjectReference` shape
BIO-BIO-002/BIO-DOC-002/BIO-DOC-003 already established for their own foundation dependency - see
this project's csproj remarks.

**NOT built on a `ProjectReference` to BIO-PHO-001.** `PhotoMaster` is a tenant-scoped, internal,
access-restricted aggregate (`PhotoMasterService` is `Scoped`, reads a per-request tenant
context) - the opposite shape from BIO-BIO-001's plain contracts library. This story defines its
own narrow `Domain/IPhotoMasterExistenceGateway.cs` instead, wired in
`Modules/BioBio003Module.cs` to a real adapter wrapping BIO-PHO-001's already-registered
`PhotoMasterService.GetVersionAsync` - the identical "narrow gateway + host-level adapter" shape
`BioPho001Module` itself already uses for ITS OWN dependency on BIO-IDN-001
(`IPersonExistenceGateway`), and that six other stories (BIO-IDN-002 through 006, BIO-PHO-002)
have used since.

**Face count and the seven quality checks are deliberately separate concepts.** AC01's "exactly
one face" is a precondition on whether the seven AC02 checks are even meaningful (a quality
opinion about a face that does not exist, or one of several, is undefined) - modelling it as an
eighth member of `FaceQualityCheck` would blur a precondition with a peer check.

**Liveness is a real approval gate, not a recorded-but-ignored field.** AC05's wording
("supported") could be read as merely requiring the interface to exist; this implementation goes
further and makes a failed liveness check block approval exactly like a failed quality check
does, because a face biometric reference approved from a detectably non-live capture would defeat
the purpose of having a liveness hook at all. A later story that wants a softer "record but don't
block" policy can change `FaceEnrolmentService.CaptureAsync`'s single `if` without touching the
`ILivenessDetector` contract.

**"Approved reference" is a typed outcome, never an exception (AC04).** `FaceCaptureOutcome`
distinguishes an ordinary, expected "recapture needed" result from a genuine error (a missing
source photo throws `FaceEnrolmentException`, since that is a caller/integration mistake, not a
quality-gate outcome an operator handles routinely).

**No audit trail.** Unlike BIO-BIO-002 (whose AC05 explicitly requires "Enrolment is audited"),
none of this story's five ACs mention auditing, so `FaceEnrolmentService` adds none - a
deliberate scope decision, not an oversight. A later story that adds one does not need this
service's shape to change.

**No HTTP endpoint is mapped by this story**, the same judgment call BIO-BIO-001/002 made:
AC01-AC05 are about the capture/quality-gate model, not an HTTP surface, and the service takes a
caller-resolved tenant/person id rather than resolving one itself.

**Identifiers are plain strings**, the same explicitly documented choice BIO-BIO-001/002/
BIO-DOC-001 made; no `identifier-registry.json` prefix is registered for a face biometric
reference (frozen, ADR-governed contract, out of this story's `read_only_shared_contracts`
scope).

**Every sample byte array in this suite is synthetic filler**, never a captured face image (this
story's own top-level constraint: "Use synthetic data only").
