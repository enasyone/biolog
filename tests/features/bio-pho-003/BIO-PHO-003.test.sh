#!/usr/bin/env bash
# Offline-safe structural checks for BIO-PHO-003 (photo quality and biometric suitability).
# Behavioural assertions (print/biometric verdicts, operator decision, promotion, HTTP pipeline)
# live in the executable xunit suites under tests/features/bio-pho-003/ and
# tests/dotnet/Biolog.IntegrationTests/PhotoQualityHostChannelTests.cs (see
# docs/features/bio-pho-003/README.md).
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"
pass=0; fail=0
check() { local label="$1"; shift; if "$@"; then echo "PASS  $label"; pass=$((pass+1)); else echo "FAIL  $label"; fail=$((fail+1)); fi; }

FEATURE=api/features/bio-pho-003
TESTS=tests/features/bio-pho-003

# --- AC01: a photo can pass print and fail biometric quality, or vice versa ---
check "AC01 feature project exists" test -f "$FEATURE/Biolog.Features.PhotoQuality.csproj"
check "AC01 project targets the pinned .NET SDK's TFM" bash -c "grep -q '<TargetFramework>net10.0</TargetFramework>' '$FEATURE/Biolog.Features.PhotoQuality.csproj'"
check "AC01 print quality and biometric suitability are two distinct, independent verdicts on the capture" \
  bash -c "grep -q 'PrintQualityPassed' $FEATURE/Domain/PhotoQualityCapture.cs && grep -q 'BiometricSuitabilityPassed' $FEATURE/Domain/PhotoQualityCapture.cs"
check "AC01 a capture always starts PENDING regardless of either verdict (no automatic combined gate)" \
  bash -c "grep -q 'status: PhotoQualityCaptureStatus.Pending' $FEATURE/PhotoQualityService.cs"
check "AC01 print quality is measured from the photo's own bytes, independent of biometric assessment" \
  bash -c "grep -q 'PrintQualityGate.Evaluate' $FEATURE/PhotoQualityService.cs && grep -q 'biometricGateway.AssessAsync' $FEATURE/PhotoQualityService.cs"
check "AC01 biometric suitability is a real seam to BIO-BIO-003's own quality/liveness checkers, not a feature-local re-implementation" \
  bash -c "grep -q 'IFaceQualityChecker' api/Biolog.Api/Modules/BioPho003Module.cs && grep -q 'ILivenessDetector' api/Biolog.Api/Modules/BioPho003Module.cs"
check "AC01 tests prove print-pass/biometric-fail and print-fail/biometric-pass both independently occur" \
  bash -c "grep -q 'AC01_a_photo_that_passes_print_but_fails_biometric_quality_is_recorded_pending_with_both_verdicts_distinct' $TESTS/PhotoQualityServiceTests.cs &&
           grep -q 'AC01_a_photo_that_fails_print_but_passes_biometric_quality_is_recorded_pending_with_both_verdicts_distinct' $TESTS/PhotoQualityServiceTests.cs"
check "AC01 module lives one-way in api/Biolog.Api/Modules, not back in the feature project" \
  bash -c "test -f api/Biolog.Api/Modules/BioPho003Module.cs && ! test -f '$FEATURE/BioPho003Module.cs'"
check "AC01 registered in the packaged Program.cs module array" \
  bash -c "grep -q 'new BioPho003Module()' api/Biolog.Api/Program.cs"

# --- AC02: reasons are visible ---
check "AC02 the API response carries both verdicts with their own reasons" \
  bash -c "grep -q 'print_quality' $FEATURE/Api/PhotoQualityDtos.cs && grep -q 'biometric_suitability' $FEATURE/Api/PhotoQualityDtos.cs && grep -q 'reasons' $FEATURE/Api/PhotoQualityDtos.cs"
check "AC02 a test proves reasons are visible for failures on either side" \
  bash -c "grep -q 'AC02_reasons_are_visible_for_every_failed_check_on_either_side' $TESTS/PhotoQualityServiceTests.cs"
check "AC02 a real HTTP test proves the failing print reason text is surfaced" \
  bash -c "grep -q 'AC02_a_real_http_submission_with_a_failing_print_result_surfaces_the_reason_text' $TESTS/PhotoQualityEndpointsIntegrationTests.cs"

# --- AC03: approved operator decision is audited ---
check "AC03 approve and reject are explicit, separate operator decisions, never automatic" \
  bash -c "grep -q 'public async Task<PhotoQualityCapture> ApproveAsync' $FEATURE/PhotoQualityService.cs && grep -q 'public async Task<PhotoQualityCapture> RejectAsync' $FEATURE/PhotoQualityService.cs"
check "AC03 approval promotes a real BIO-PHO-001 PhotoMaster, reachable only from ApproveAsync" \
  bash -c "grep -c 'promotionGateway.PromoteAsync' $FEATURE/PhotoQualityService.cs | grep -q '^1$'"
check "AC03 every decision is audited before the repository commit (ADR-0003 Decision 7)" \
  bash -c "grep -q 'photo_quality.capture.approve' $FEATURE/PhotoQualityService.cs && grep -q 'photo_quality.capture.reject' $FEATURE/PhotoQualityService.cs"
check "AC03 only a PENDING capture may be decided, enforced at the repository boundary too" \
  bash -c "grep -q 'PhotoQualityCaptureStatus.Pending' $FEATURE/Domain/InMemoryPhotoQualityCaptureRepository.cs"
check "AC03 rejecting requires a non-empty reason" \
  bash -c "grep -q 'reason is required to reject a capture' $FEATURE/PhotoQualityService.cs"
check "AC03 a test proves approval promotes and is audited" \
  bash -c "grep -q 'AC03_approving_a_pending_capture_promotes_a_real_photo_master_and_is_audited' $TESTS/PhotoQualityServiceTests.cs"
check "AC03 a test proves rejection requires a reason and is audited without promoting" \
  bash -c "grep -q 'AC03_rejecting_a_pending_capture_requires_a_reason_and_is_audited_without_promoting_anything' $TESTS/PhotoQualityServiceTests.cs"
check "AC03 a test proves an already-decided capture cannot be decided again" \
  bash -c "grep -q 'AC03_a_capture_already_decided_cannot_be_decided_again' $TESTS/PhotoQualityServiceTests.cs"
check "AC03 a real host-channel test proves approval creates a real, independently readable BIO-PHO-001 photo master" \
  bash -c "grep -q 'AC03_approving_a_real_capture_creates_a_real_independently_readable_BIOPHO001_photo_master' tests/dotnet/Biolog.IntegrationTests/PhotoQualityHostChannelTests.cs"

# --- AC04: no beauty/facial reshaping filters are permitted ---
# Grepped against actual <PackageReference>/<ProjectReference> lines only, not prose - this
# story's own doc comments name these same libraries when explaining the disclosed boundary
# (e.g. "no SixLabors.ImageSharp..."), which a comment-blind grep would self-match.
check "AC04 no image-processing or computer-vision library dependency exists anywhere in this feature" \
  bash -c "! grep -Ei '<(Package|Project)Reference' $FEATURE/Biolog.Features.PhotoQuality.csproj | grep -Eiq 'SixLabors|SkiaSharp|System\\.Drawing|OpenCV'"
check "AC04 the raster inspector reads header metadata only, never decodes/re-encodes pixels (disclosed boundary)" \
  bash -c "grep -q 'No pixel resampling or content analysis' $FEATURE/Domain/PhotoQualityRasterInspector.cs"

# --- ZB-PHO003-01 (security round 1): provenance+RBAC gate on every route, reads included ---
check "a feature-local ActorProvenanceGate exists, the canonical HMAC pattern, never a dummy key" \
  bash -c "grep -q 'HmacActorProvenanceStore' $FEATURE/Api/ActorProvenanceGate.cs && grep -q 'MinimumSecretLength' $FEATURE/Api/ActorProvenanceGate.cs"
check "a feature-local IPermissionGateway exists, wrapping the real RBAC grant lookup" \
  bash -c "grep -q 'IPermissionGateway' $FEATURE/Domain/IPermissionGateway.cs && grep -q 'ActorHasPermissionAsync' $FEATURE/Domain/IPermissionGateway.cs"
check "three distinct least-privilege permission keys exist (submit/decide/view), not one bundled grant" \
  bash -c "grep -q 'photo_quality.capture.submit' $FEATURE/Domain/IPermissionGateway.cs && grep -q 'photo_quality.capture.decide' $FEATURE/Domain/IPermissionGateway.cs && grep -q 'photo_quality.capture.view' $FEATURE/Domain/IPermissionGateway.cs"
check "every one of this feature's five routes calls AuthorizeAsync (provenance + permission), reads included" \
  bash -c "grep -c 'AuthorizeAsync(' $FEATURE/Api/PhotoQualityEndpoints.cs | grep -qE '^[5-9]$'"
check "the module registers the real RBAC-backed PermissionGatewayAdapter, wrapping BIO-IDN-006's own PermissionService" \
  bash -c "grep -q 'PermissionGatewayAdapter' api/Biolog.Api/Modules/BioPho003Module.cs && grep -q 'Biolog.Features.Rbac.PermissionService' api/Biolog.Api/Modules/BioPho003Module.cs"
check "the module seeds a real RolePermissionGrant for the demo web bridge's enrolment-staff role" \
  bash -c "grep -q 'RoleType.Enrolment' api/Biolog.Api/Modules/BioPho003Module.cs && grep -q 'WebSessionBridge:SyntheticTenantId' api/Biolog.Api/Modules/BioPho003Module.cs"
check "web/lib/actor-provenance-routes.ts carries this feature's own PhotoQuality:ActorProvenanceSecret entry (TD-WEB-PROVENANCE-BRIDGE)" \
  bash -c "grep -q \"PhotoQuality:ActorProvenanceSecret\" web/lib/actor-provenance-routes.ts && grep -q \"prefix('/api/v1/photo-quality')\" web/lib/actor-provenance-routes.ts"
check "a real host-channel test proves missing/forged provenance is rejected and an unprivileged real actor is denied, not substituted" \
  bash -c "grep -q 'ZB_PHO003_01_a_submission_with_no_provenance_proof_at_all_is_rejected' tests/dotnet/Biolog.IntegrationTests/PhotoQualityHostChannelTests.cs &&
           grep -q 'ZB_PHO003_01_a_forged_provenance_proof_under_the_wrong_secret_is_also_rejected' tests/dotnet/Biolog.IntegrationTests/PhotoQualityHostChannelTests.cs &&
           grep -q 'with_a_valid_provenance_proof_but_no_grant_is_denied_403_real_rbac_not_substituted' tests/dotnet/Biolog.IntegrationTests/PhotoQualityHostChannelTests.cs"

# --- ZB-PHO003-02/03/04 and SY-PHO003-07, resolved by Kemi's ruling: a local IIdempotencyStore,
#     never reconstruction from BIO-PHO-001's own post-hoc state ---
check "a capture can durably carry an in-flight-approval marker (attempt id, immutable initiating actor) while still PENDING" \
  bash -c "grep -q 'PendingApprovalAttemptId' $FEATURE/Domain/PhotoQualityCapture.cs && grep -q 'PendingApprovalActorId' $FEATURE/Domain/PhotoQualityCapture.cs"
check "the marker is written BEFORE ever calling PromoteAsync, not after" \
  bash -c "awk '/pendingApprovalAttemptId: attemptId/{marker=NR} /promotionGateway\.PromoteAsync\(/{promote=NR} END{exit !(marker && promote && marker<promote)}' $FEATURE/PhotoQualityService.cs"
check "this feature owns its own fresh, project-local IIdempotencyStore (the same BIO-CRD-001/BIO-PHO-002 copy-per-feature convention)" \
  bash -c "test -f $FEATURE/Domain/IdempotencyStore.cs && grep -q 'IIdempotencyStore' $FEATURE/Domain/IdempotencyStore.cs && grep -q 'InMemoryIdempotencyStore' $FEATURE/Domain/IdempotencyStore.cs"
check "ApproveAsync Claims the attempt id as an idempotency key BEFORE calling PromoteAsync, branching on IsNewClaim not merely State" \
  bash -c "grep -q 'idempotencyStore.Claim(' $FEATURE/PhotoQualityService.cs && grep -q 'claim.IsNewClaim' $FEATURE/PhotoQualityService.cs"
check "a completed claim returns its stored result directly - zero further calls to IPhotoMasterPromotionGateway on a resumed retry" \
  bash -c "grep -q 'IdempotencyRecordState.Completed' $FEATURE/PhotoQualityService.cs"
check "a failed promotion releases its claim so a genuine retry can actually re-attempt, not wait out a pointless timeout" \
  bash -c "grep -q 'idempotencyStore.Release(' $FEATURE/PhotoQualityService.cs"
check "IPhotoMasterPromotionGateway is back to a strictly SMALLER interface - just PromoteAsync, no reconstruction methods added" \
  bash -c "! grep -q 'FindPromotedVersionAsync' $FEATURE/Domain/IPhotoMasterPromotionGateway.cs && grep -c 'Task<' $FEATURE/Domain/IPhotoMasterPromotionGateway.cs | grep -qE '^1$'"
check "the now-obsolete baseline/content-equality/audit-trail-reconstruction heuristics are gone entirely, not left alongside the idempotency store" \
  bash -c "! grep -q 'PendingApprovalBaselineMasterVersion' $FEATURE/Domain/PhotoQualityCapture.cs && ! grep -q 'PromotionAmbiguous' $FEATURE/Domain/PhotoQualityError.cs && ! grep -q 'FindPromotedVersionAsync' $FEATURE/PhotoQualityService.cs"
check "BIO-PHO-001's own module registration is back to its original single-line IPhotoAuditSink shape - no leftover concrete-type registration this story no longer needs" \
  bash -c "! grep -q 'AddSingleton<InMemoryPhotoAuditSink>' api/Biolog.Api/Modules/BioPho001Module.cs"
check "a retry under a DIFFERENT authorized actor still credits the ORIGINAL initiator, never the retrying operator" \
  bash -c "grep -q 'initiatingActor' $FEATURE/PhotoQualityService.cs"
check "rejecting a capture whose approval is already in progress is refused, never silently orphaning a promoted master" \
  bash -c "grep -q 'ApprovalAlreadyInProgress' $FEATURE/PhotoQualityService.cs && grep -q 'ApprovalAlreadyInProgress' $FEATURE/Domain/PhotoQualityError.cs"
check "tests prove crashes before the marker, after promotion, and after audit all converge safely, plus a failed promotion is genuinely retriable" \
  bash -c "grep -q 'ZB_PHO003_02_a_crash_before_the_marker_is_ever_written' $TESTS/PhotoQualityServiceTests.cs &&
           grep -q 'ZB_PHO003_02_a_failed_promotion_releases_its_claim' $TESTS/PhotoQualityServiceTests.cs &&
           grep -q 'ZB_PHO003_02_a_crash_after_promotion_but_before_the_audit_write' $TESTS/PhotoQualityServiceTests.cs &&
           grep -q 'ZB_PHO003_02_a_crash_after_the_audit_write_but_before_the_final_commit' $TESTS/PhotoQualityServiceTests.cs"
check "Kemi's own named residual (a crash strictly between promotion and Complete()) fails loud via a bounded timeout, not silently" \
  bash -c "grep -q 'IdempotencyWaitTimeout' $FEATURE/PhotoQualityService.cs && grep -q 'IdempotencyWaitTimeout' $FEATURE/Domain/PhotoQualityError.cs &&
           grep -q 'ZB_PHO003_04_a_crash_strictly_between_promotion_succeeding_and_the_local_claim_being_completed' $TESTS/PhotoQualityServiceTests.cs"
check "a test proves rejecting an in-progress approval is refused" \
  bash -c "grep -q 'ZB_PHO003_02_rejecting_a_capture_whose_approval_is_already_in_progress_is_refused' $TESTS/PhotoQualityServiceTests.cs"

# --- AC05: UI-first - connected through PhotoService ---
check "AC05 the web-side PhotosPort is extended with real photo-quality methods" \
  bash -c "grep -q 'submitCapture\\|listCaptures' web/features/photos/ports.ts"
check "AC05 a live adapter calls the real backend endpoint" \
  bash -c "grep -q '/api/v1/photo-quality/captures' web/features/photos/live.ts"
check "AC05 next.config.mjs forwards the photo-quality prefix to the real backend" \
  bash -c "grep -q 'photo-quality' web/next.config.mjs"

# --- Cross-feature dependency wiring ---
check "no ProjectReference to any other feature project (all cross-feature access is through this project's own gateways)" \
  bash -c "! grep -q '<ProjectReference' $FEATURE/Biolog.Features.PhotoQuality.csproj"
check "this feature's endpoint group maps no mutation route beyond submit/approve/reject" \
  bash -c "grep -c 'MapPost' $FEATURE/Api/PhotoQualityEndpoints.cs | grep -qE '^[3]$'"
# Excludes this project's own /// doc comments, which name FaceEnrolmentService in prose when
# explaining that it is deliberately never called - a comment-blind grep would self-match.
check "this story never calls BIO-BIO-003's FaceEnrolmentService.CaptureAsync (no FaceBiometricReference is ever committed from here)" \
  bash -c "! grep -rh --include='*.cs' -v '^\\s*///' $FEATURE api/Biolog.Api/Modules/BioPho003Module.cs | grep -q 'FaceEnrolmentService'"

# --- Developer test wiring ---
check "unit tests exist for the service, and integration tests exercise the real HTTP endpoints (self-hosted)" bash -c \
  'test -f tests/features/bio-pho-003/PhotoQualityServiceTests.cs &&
   test -f tests/features/bio-pho-003/PhotoQualityEndpointsIntegrationTests.cs'
check "a real packaged-host channel test exists (not merely an injected test double)" \
  test -f tests/dotnet/Biolog.IntegrationTests/PhotoQualityHostChannelTests.cs
check "all fixtures marked SYNTHETIC" bash -c \
  'grep -rq SYNTHETIC tests/features/bio-pho-003 tests/dotnet/Biolog.IntegrationTests/PhotoQualityHostChannelTests.cs'

check "builder evidence recorded" test -d evidence/BIO-PHO-003/builder

echo "=============================="
echo "$pass passed, $fail failed"
[ "$fail" -eq 0 ]
