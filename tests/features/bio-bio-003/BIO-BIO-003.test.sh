#!/usr/bin/env bash
# Offline-safe structural checks for BIO-BIO-003 (quality-gated face capture). Behavioural
# assertions live in the executable xunit suite under tests/features/bio-bio-003/ (see
# docs/features/bio-bio-003/README.md).
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"
pass=0; fail=0
check() { local label="$1"; shift; if "$@"; then echo "PASS  $label"; pass=$((pass+1)); else echo "FAIL  $label"; fail=$((fail+1)); fi; }

FEATURE=api/features/bio-bio-003/Biolog.Features.FaceEnrolment

# --- AC01: exactly one face is required ---
check "AC01 feature project exists" test -f "$FEATURE/Biolog.Features.FaceEnrolment.csproj"
check "AC01 project targets the pinned .NET SDK's TFM" bash -c "grep -q '<TargetFramework>net10.0</TargetFramework>' '$FEATURE/Biolog.Features.FaceEnrolment.csproj'"
check "AC01 the assessment exposes a face-count precondition distinct from the seven checks" \
  bash -c "grep -q 'int FaceCount' '$FEATURE/Domain/FaceQualityAssessment.cs' && grep -q 'FaceCountApproved => FaceCount == 1' '$FEATURE/Domain/FaceQualityAssessment.cs'"
check "AC01 a structural test proves zero and multiple faces are both rejected" \
  bash -c "grep -q 'AC01_zero_faces_is_not_approved' tests/features/bio-bio-003/FaceQualityAssessmentTests.cs && grep -q 'AC01_multiple_faces_is_not_approved' tests/features/bio-bio-003/FaceQualityAssessmentTests.cs"

# --- AC02: pose, eyes, lighting, sharpness, resolution, occlusion, background checks ---
check "AC02 exactly the seven named checks exist as a closed enum" \
  bash -c "for c in Pose Eyes Lighting Sharpness Resolution Occlusion Background; do grep -q \"    \$c,\" '$FEATURE/Domain/FaceQualityCheck.cs' || exit 1; done && [ \"\$(grep -c '^    [A-Za-z]*,\$' '$FEATURE/Domain/FaceQualityCheck.cs')\" = 7 ]"
check "AC02 a quality checker contract exists, separate from the service" test -f "$FEATURE/Quality/IFaceQualityChecker.cs"
check "AC02 a synthetic quality checker implementation exists (local-dev/AC-05-precedent requirement)" \
  bash -c "test -f '$FEATURE/Quality/SyntheticFaceQualityChecker.cs' && grep -q 'IFaceQualityChecker' '$FEATURE/Quality/SyntheticFaceQualityChecker.cs'"
check "AC02 a structural test proves each of the seven checks independently blocks approval" \
  bash -c "grep -q 'AC02_a_single_failed_check_blocks_approval' tests/features/bio-bio-003/FaceQualityAssessmentTests.cs"

# --- AC03: approved reference retains model/version and source-photo linkage ---
check "AC03 the approved reference type exists and is immutable (no public constructor)" \
  bash -c "grep -q 'internal FaceBiometricReference(' '$FEATURE/Domain/FaceBiometricReference.cs'"
check "AC03 model/version is carried via BIO-BIO-001's own BiometricTemplate, not re-derived" \
  bash -c "grep -q 'BiometricTemplate Template' '$FEATURE/Domain/FaceBiometricReference.cs'"
check "AC03 source-photo linkage is a dedicated field" \
  bash -c "grep -q 'int SourcePhotoMasterVersion' '$FEATURE/Domain/FaceBiometricReference.cs'"
check "AC03 a narrow photo-master existence gateway exists, not a ProjectReference to BIO-PHO-001" \
  bash -c "test -f '$FEATURE/Domain/IPhotoMasterExistenceGateway.cs' && ! grep -q '<ProjectReference[^>]*bio-pho-001' '$FEATURE/Biolog.Features.FaceEnrolment.csproj'"
check "AC03 the service verifies the source photo exists before approving anything" \
  bash -c "grep -q 'photoMasterGateway.ExistsAsync' '$FEATURE/Service/FaceEnrolmentService.cs'"
check "AC03 a structural test proves model/version and linkage are both retained" \
  bash -c "grep -q 'AC03_an_approved_capture_retains_model_version_and_source_photo_linkage' tests/features/bio-bio-003/FaceEnrolmentServiceTests.cs"

# --- AC04: poor capture yields actionable recapture guidance ---
check "AC04 a failed check cannot be constructed without guidance" \
  bash -c "grep -q 'a failed check must carry actionable guidance' '$FEATURE/Domain/FaceQualityCheckResult.cs'"
check "AC04 rejection is a typed outcome, not a thrown exception" \
  bash -c "grep -q 'FaceCaptureOutcome.Reject' '$FEATURE/Service/FaceEnrolmentService.cs' && grep -q 'IReadOnlyList<string> RecaptureGuidance' '$FEATURE/Service/FaceCaptureOutcome.cs'"
check "AC04 a structural test proves a poor capture yields guidance instead of throwing" \
  bash -c "grep -q 'AC04_zero_faces_yields_guidance_instead_of_throwing' tests/features/bio-bio-003/FaceEnrolmentServiceTests.cs"

# --- AC05: liveness hook is supported without hard-coding a vendor ---
check "AC05 a liveness detector contract exists, separate from the service" test -f "$FEATURE/Liveness/ILivenessDetector.cs"
check "AC05 a synthetic liveness implementation exists, no vendor SDK referenced" \
  bash -c "test -f '$FEATURE/Liveness/SyntheticLivenessDetector.cs' && ! grep -q '<PackageReference' '$FEATURE/Biolog.Features.FaceEnrolment.csproj'"
check "AC05 the service calls the liveness hook and it participates in the approval gate" \
  bash -c "grep -q 'livenessDetector.DetectAsync' '$FEATURE/Service/FaceEnrolmentService.cs' && grep -q '!liveness.IsLive' '$FEATURE/Service/FaceEnrolmentService.cs'"
check "AC05 a structural test proves a failed liveness check blocks approval even when quality passes" \
  bash -c "grep -q 'AC05_a_failed_liveness_check_blocks_approval_even_when_quality_passes' tests/features/bio-bio-003/FaceEnrolmentServiceTests.cs"

# --- Governance (BIO-IDN-001-QA-01 / BIO-PHO-001-QA-01 shape) ---
check "governance: the repository and its implementation are internal, not public" \
  bash -c "grep -q '^internal interface IFaceBiometricReferenceRepository' '$FEATURE/Domain/IFaceBiometricReferenceRepository.cs' && grep -q '^internal sealed class InMemoryFaceBiometricReferenceRepository' '$FEATURE/Domain/InMemoryFaceBiometricReferenceRepository.cs'"
check "governance: the service is internal too" \
  bash -c "grep -q '^internal sealed class FaceEnrolmentService' '$FEATURE/Service/FaceEnrolmentService.cs'"
check "governance: the InternalsVisibleTo seam is explicit and auditable in source control" \
  bash -c "grep -q '<InternalsVisibleTo Include=\"Biolog.Api\" />' '$FEATURE/Biolog.Features.FaceEnrolment.csproj' && grep -q '<InternalsVisibleTo Include=\"Biolog.Features.FaceEnrolment.Tests\" />' '$FEATURE/Biolog.Features.FaceEnrolment.csproj'"
check "governance: reflection tests prove the repository/service are inaccessible to ordinary production consumers" \
  bash -c "grep -q 'IFaceBiometricReferenceRepository_and_InMemoryFaceBiometricReferenceRepository_are_not_public' tests/features/bio-bio-003/FaceEnrolmentGovernanceTests.cs"
check "governance: cross-tenant lookup returns null, not an exception (no enumeration oracle)" \
  bash -c "grep -q 'Cross-tenant lookup returns null' '$FEATURE/Domain/InMemoryFaceBiometricReferenceRepository.cs'"

# --- Feature-to-feature dependency shape ---
check "this project references BIO-BIO-001's provider-neutral biometric library" \
  bash -c "grep -q 'Biolog.Features.Biometrics.csproj' '$FEATURE/Biolog.Features.FaceEnrolment.csproj'"
check "module project references stay one-way (no ProjectReference back to Biolog.Api)" \
  bash -c "! grep -q '<ProjectReference[^>]*Biolog\.Api\.csproj' '$FEATURE/Biolog.Features.FaceEnrolment.csproj'"

# --- Module wiring ---
check "module lives one-way in api/Biolog.Api/Modules, not back in the feature project" \
  bash -c "test -f api/Biolog.Api/Modules/BioBio003Module.cs && ! test -f '$FEATURE/BioBio003Module.cs'"
check "registered in the packaged Program.cs module array" \
  bash -c "grep -q 'new BioBio003Module()' api/Biolog.Api/Program.cs"
check "no HTTP endpoints are mapped (AC01-AC05 are about the capture model, matching BIO-BIO-001/002's deferred-integration call)" \
  bash -c "! grep -q 'endpoints\.Map' api/Biolog.Api/Modules/BioBio003Module.cs"
check "the gateway adapter wraps BIO-PHO-001's own already-registered PhotoMasterService" \
  bash -c "grep -q 'PhotoMasterService.GetVersionAsync' api/Biolog.Api/Modules/BioBio003Module.cs"

# --- Developer test wiring ---
check "unit tests reference the feature project" \
  bash -c "grep -q 'Biolog.Features.FaceEnrolment.csproj' tests/features/bio-bio-003/Biolog.Features.FaceEnrolment.Tests.csproj"
check "docs/features/bio-bio-003/README.md documents the feature" test -f docs/features/bio-bio-003/README.md
check "builder evidence recorded" test -d evidence/features/bio-bio-003/builder

echo "=============================="
echo "$pass passed, $fail failed"
[ "$fail" -eq 0 ]
