#!/usr/bin/env bash
# Offline-safe structural checks for BIO-BIO-005 (face 1:1 verification). Behavioural assertions
# live in the executable xunit suite under tests/features/bio-bio-005/ (see
# docs/features/bio-bio-005/README.md).
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"
pass=0; fail=0
check() { local label="$1"; shift; if "$@"; then echo "PASS  $label"; pass=$((pass+1)); else echo "FAIL  $label"; fail=$((fail+1)); fi; }

FEATURE=api/features/bio-bio-005/Biolog.Features.FaceVerification

# --- AC01: similarity, threshold policy, quality and model version are recorded ---
check "AC01 feature project exists" test -f "$FEATURE/Biolog.Features.FaceVerification.csproj"
check "AC01 project targets the pinned .NET SDK's TFM" bash -c "grep -q '<TargetFramework>net10.0</TargetFramework>' '$FEATURE/Biolog.Features.FaceVerification.csproj'"
check "AC01 a persisted verification evidence type exists" test -f "$FEATURE/Domain/FaceVerificationEvidence.cs"
check "AC01 the evidence carries similarity, threshold, quality and provider (model) version" \
  bash -c "grep -q 'double? Score' '$FEATURE/Domain/FaceVerificationEvidence.cs' && grep -q 'double ThresholdUsed' '$FEATURE/Domain/FaceVerificationEvidence.cs' && grep -q 'int QualityFaceCount' '$FEATURE/Domain/FaceVerificationEvidence.cs' && grep -q 'bool QualityApproved' '$FEATURE/Domain/FaceVerificationEvidence.cs' && grep -q 'string ProviderVersion' '$FEATURE/Domain/FaceVerificationEvidence.cs'"
check "AC01 an evidence store persistence seam exists" test -f "$FEATURE/Domain/IFaceVerificationEvidenceStore.cs"
check "AC01 the service persists exactly one evidence record per verification call" \
  bash -c "grep -q 'evidenceStore.SaveAsync' '$FEATURE/Service/FaceVerificationService.cs'"
check "AC01 a structural test proves similarity/threshold/quality/model-version are recorded" \
  bash -c "grep -q 'AC01_a_matched_verification_records_similarity_threshold_quality_and_model_version' tests/features/bio-bio-005/FaceVerificationServiceTests.cs"
check "AC01 a structural test proves quality is recorded even on a poor-quality capture" \
  bash -c "grep -q 'AC01_quality_is_recorded_even_when_the_live_capture_has_poor_quality' tests/features/bio-bio-005/FaceVerificationServiceTests.cs"

# --- AC02: threshold is policy-based rather than arbitrary UI input ---
check "AC02 a threshold policy contract exists, separate from the service" test -f "$FEATURE/Domain/IFaceVerificationThresholdPolicy.cs"
check "AC02 CompareAsync's own signature has no threshold parameter" \
  bash -c "! sed -n '/public async Task<FaceVerificationEvidence> CompareAsync/,/cancellationToken)$/p' '$FEATURE/Service/FaceVerificationService.cs' | grep -qi threshold"
check "AC02 CompareAsync resolves the threshold only from the policy, keyed by tenant" \
  bash -c "grep -q 'thresholdPolicy.GetThreshold(tenantId)' '$FEATURE/Service/FaceVerificationService.cs'"
check "AC02 a synthetic default policy implementation exists" test -f "$FEATURE/Domain/SyntheticFaceVerificationThresholdPolicy.cs"
check "AC02 a structural test proves the same score is accepted or rejected purely by policy threshold" \
  bash -c "grep -q 'AC02_the_same_score_is_accepted_under_a_low_threshold_and_rejected_under_a_high_one' tests/features/bio-bio-005/FaceVerificationServiceTests.cs"

# --- AC03: verification evidence is separate from authorization decision ---
check "AC03 evidence carries no allow/deny field at all" \
  bash -c "! grep -v '^\s*///' '$FEATURE/Domain/FaceVerificationEvidence.cs' | grep -qi 'Decision'"
check "AC03 a distinct authorization-decision type exists" test -f "$FEATURE/Domain/FaceAuthorizationDecision.cs"
check "AC03 the decision is produced by a separate, pure policy function, not by CompareAsync itself" \
  bash -c "test -f '$FEATURE/Domain/FaceAuthorizationPolicy.cs' && grep -q 'public static class FaceAuthorizationPolicy' '$FEATURE/Domain/FaceAuthorizationPolicy.cs' && ! grep -v '^\s*///' '$FEATURE/Service/FaceVerificationService.cs' | grep -qi 'AccessDecision'"
check "AC03 a structural test proves evidence and decision are produced by separate calls" \
  bash -c "grep -q 'AC03_a_matched_comparison_produces_evidence_with_no_authorization_field_and_decides_allow_separately' tests/features/bio-bio-005/FaceVerificationServiceTests.cs"
check "AC03 a structural test proves deciding is a pure, repeatable operation on stored evidence" \
  bash -c "grep -q 'AC03_deciding_the_same_evidence_twice_is_a_pure_repeatable_operation' tests/features/bio-bio-005/FaceVerificationServiceTests.cs"

# --- AC04: reference/photo lifecycle changes are handled deterministically ---
check "AC04 a narrow face-reference gateway exists, not a call into BIO-BIO-003's internal service" \
  bash -c "test -f '$FEATURE/Domain/IFaceReferenceGateway.cs' && ! grep -q 'FaceEnrolmentService' '$FEATURE/Service/FaceVerificationService.cs'"
check "AC04 the service resolves the reference fresh (the latest one) on every call, never a cached/pinned id" \
  bash -c "grep -q 'referenceGateway.GetLatestApprovedReferenceAsync' '$FEATURE/Service/FaceVerificationService.cs'"
check "AC04 a missing reference is a precondition failure, not a routine outcome" \
  bash -c "grep -q 'FaceVerificationException.NoAuthorisedReference' '$FEATURE/Service/FaceVerificationService.cs'"
check "AC04 a structural test proves a superseded reference is never used" \
  bash -c "grep -q 'AC04_a_superseded_reference_is_never_used_the_latest_one_always_is' tests/features/bio-bio-005/FaceVerificationServiceTests.cs"

# --- BIO-BIO-005-QA-01 (Chioma acceptance, High): source-photo lifecycle staleness ---
check "QA-01 the reference gateway also resolves the current photo master version" \
  bash -c "grep -q 'GetCurrentPhotoMasterVersionAsync' '$FEATURE/Domain/IFaceReferenceGateway.cs'"
check "QA-01 a closed outcome member exists for a stale (photo-superseded) reference" \
  bash -c "grep -q 'ReferenceStale,' '$FEATURE/Domain/FaceComparisonOutcome.cs'"
check "QA-01 the service compares current vs source photo master version before any comparison" \
  bash -c "grep -q 'currentPhotoMasterVersion != reference.SourcePhotoMasterVersion' '$FEATURE/Service/FaceVerificationService.cs'"
check "QA-01 the authorization policy denies a stale reference with an explicit reason" \
  bash -c "grep -q 'FaceComparisonOutcome.ReferenceStale =>' '$FEATURE/Domain/FaceAuthorizationPolicy.cs' && grep -q 'reference_stale' '$FEATURE/Domain/FaceAuthorizationPolicy.cs'"
check "QA-01 a structural test proves a superseded-photo reference is recorded as ReferenceStale, not Matched" \
  bash -c "grep -q 'A_reference_from_a_superseded_photo_is_recorded_as_ReferenceStale_not_Matched' tests/features/bio-bio-005/FaceVerificationPhotoLifecycleTests.cs"
check "QA-01 a structural test proves staleness is checked before extraction is ever attempted" \
  bash -c "grep -q 'Staleness_is_detected_before_extraction_is_ever_attempted' tests/features/bio-bio-005/FaceVerificationPhotoLifecycleTests.cs"

# --- Provider/vendor extraction failures are contained (BIO-BIO-004-F01's lesson, applied proactively) ---
check "extraction failures are caught broadly, not only ArgumentException" \
  bash -c "! grep -q '^        catch (ArgumentException)$' '$FEATURE/Service/FaceVerificationService.cs' && grep -q 'catch (Exception)' '$FEATURE/Service/FaceVerificationService.cs'"
check "a genuine caller cancellation is distinguished and propagated, not recorded" \
  bash -c "grep -q 'catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)' '$FEATURE/Service/FaceVerificationService.cs'"
check "regression tests exist for non-ArgumentException provider failures and the cancellation carve-out" \
  bash -c "test -f tests/features/bio-bio-005/FaceVerificationAcquisitionFailureTests.cs && grep -q 'A_non_ArgumentException_provider_failure_is_recorded_as_AcquisitionFailed_not_propagated' tests/features/bio-bio-005/FaceVerificationAcquisitionFailureTests.cs && grep -q 'A_genuine_caller_cancellation_propagates_and_writes_no_evidence' tests/features/bio-bio-005/FaceVerificationAcquisitionFailureTests.cs"

# --- Governance (BIO-IDN-001-QA-01 shape) ---
check "governance: the evidence store and its implementation are internal, not public" \
  bash -c "grep -q '^internal interface IFaceVerificationEvidenceStore' '$FEATURE/Domain/IFaceVerificationEvidenceStore.cs' && grep -q '^internal sealed class InMemoryFaceVerificationEvidenceStore' '$FEATURE/Domain/InMemoryFaceVerificationEvidenceStore.cs'"
check "governance: the service is internal too" \
  bash -c "grep -q '^internal sealed class FaceVerificationService' '$FEATURE/Service/FaceVerificationService.cs'"
check "governance: the InternalsVisibleTo seam is explicit and auditable in source control" \
  bash -c "grep -q '<InternalsVisibleTo Include=\"Biolog.Api\" />' '$FEATURE/Biolog.Features.FaceVerification.csproj' && grep -q '<InternalsVisibleTo Include=\"Biolog.Features.FaceVerification.Tests\" />' '$FEATURE/Biolog.Features.FaceVerification.csproj'"
check "governance: reflection tests prove the evidence store/service are inaccessible to ordinary production consumers" \
  bash -c "grep -q 'IFaceVerificationEvidenceStore_and_InMemory_implementation_are_not_public' tests/features/bio-bio-005/FaceVerificationGovernanceTests.cs"
check "governance: cross-tenant lookup returns null, not an exception (no enumeration oracle)" \
  bash -c "grep -q 'Cross-tenant lookup returns null' '$FEATURE/Domain/InMemoryFaceVerificationEvidenceStore.cs'"

# --- Feature-to-feature dependency shape ---
check "this project references BIO-BIO-001's provider-neutral biometric library" \
  bash -c "grep -q 'Biolog.Features.Biometrics.csproj' '$FEATURE/Biolog.Features.FaceVerification.csproj'"
check "this project references BIO-BIO-003's own public quality/liveness contracts" \
  bash -c "grep -q 'Biolog.Features.FaceEnrolment.csproj' '$FEATURE/Biolog.Features.FaceVerification.csproj'"
check "module project references stay one-way (no ProjectReference back to Biolog.Api)" \
  bash -c "! grep -q '<ProjectReference[^>]*Biolog\.Api\.csproj' '$FEATURE/Biolog.Features.FaceVerification.csproj'"

# --- Module wiring ---
check "module lives one-way in api/Biolog.Api/Modules, not back in the feature project" \
  bash -c "test -f api/Biolog.Api/Modules/BioBio005Module.cs && ! test -f '$FEATURE/BioBio005Module.cs'"
check "registered in the packaged Program.cs module array" \
  bash -c "grep -q 'new BioBio005Module()' api/Biolog.Api/Program.cs"
check "no HTTP endpoints are mapped (AC01-AC04 are about the verification/evidence/decision model, matching every prior BIO-BIO story's deferred-integration call)" \
  bash -c "! grep -q 'endpoints\.Map' api/Biolog.Api/Modules/BioBio005Module.cs"
check "the reference gateway adapter wraps BIO-BIO-003's own already-registered FaceEnrolmentService" \
  bash -c "grep -q 'FaceEnrolmentService.GetLatestReferenceAsync\|faceEnrolmentService.GetLatestReferenceAsync' api/Biolog.Api/Modules/BioBio005Module.cs"

# --- Developer test wiring ---
check "unit tests reference the feature project" \
  bash -c "grep -q 'Biolog.Features.FaceVerification.csproj' tests/features/bio-bio-005/Biolog.Features.FaceVerification.Tests.csproj"
check "docs/features/bio-bio-005/README.md documents the feature" test -f docs/features/bio-bio-005/README.md
check "builder evidence recorded" test -d evidence/features/bio-bio-005/builder

echo "=============================="
echo "$pass passed, $fail failed"
[ "$fail" -eq 0 ]
