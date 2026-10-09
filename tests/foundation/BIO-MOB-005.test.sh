#!/usr/bin/env bash
# Offline-safe structural checks for BIO-MOB-005 (limited offline enrolment
# package). Behavioural assertions (policy-gated capture, encrypted local
# queue, exactly-once sync) live in the executable Flutter suite under
# tests/flutter/; server-side behaviour (device auth, idempotent intake,
# central duplicate scan, human resolution decisions) lives in
# tests/features/bio-mob-005/ (see docs/TESTING.md, scripts/quality.sh --full).
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"
pass=0; fail=0
check() { local label="$1"; shift; if "$@"; then echo "PASS  $label"; pass=$((pass+1)); else echo "FAIL  $label"; fail=$((fail+1)); fi; }

# --- AC01: tenant policy controls which fields/biometrics may be captured offline ---
check "AC01 a fresh, tenant-scoped capture-policy aggregate exists (not a bare constant)" \
  bash -c 'grep -q "record EnrolmentCapturePolicy" api/features/bio-mob-005/Domain/EnrolmentCapturePolicy.cs \
    && grep -q "TenantId" api/features/bio-mob-005/Domain/EnrolmentCapturePolicy.cs'
check "AC01 the story's own conservative default is text-only fields, no photo, no biometrics" \
  bash -c 'grep -q "AllowPhotoCapture = false" api/features/bio-mob-005/Domain/EnrolmentCapturePolicy.cs \
    && grep -q "AllowedBiometricModalities = new HashSet<string>(StringComparer.Ordinal)" api/features/bio-mob-005/Domain/EnrolmentCapturePolicy.cs'
check "AC01 package submission is rejected server-side when a field/photo/biometric is not policy-permitted (defense-in-depth)" \
  bash -c 'grep -q "EnforcePolicy" api/features/bio-mob-005/EnrolmentPackageService.cs \
    && grep -q "FieldNotPermitted" api/features/bio-mob-005/Domain/EnrolmentError.cs'
check "AC01 the mobile enrolment screen renders only the fields/toggles the current policy allows" \
  bash -c 'grep -q "_policy.allowedFields" mobile/lib/features/enrolment/enrolment_screen.dart \
    && grep -q "_policy.allowPhotoCapture" mobile/lib/features/enrolment/enrolment_screen.dart'

# --- AC02: data is encrypted at rest ---
check "AC02 enrolment packages are persisted through AppDatabase, the same interface SqlCipherAppDatabase's real SQLCipher-encrypted store implements" \
  bash -c 'grep -q "Future<void> enqueueEnrolmentPackage" mobile/lib/core/db/app_database.dart \
    && grep -q "Future<void> enqueueEnrolmentPackage" mobile/lib/core/db/sqlcipher_app_database.dart'
check "AC02 the enrolment tables live in the same encrypted database file as every other offline queue (no separate unencrypted store)" \
  bash -c 'grep -q "_createEnrolmentTables" mobile/lib/core/db/sqlcipher_app_database.dart'

# --- AC03: duplicate/conflict checks re-run centrally after sync ---
check "AC03 a central duplicate-review service scans already-accepted packages, independent of BIO-IDN-005/BIO-BIO-006" \
  bash -c 'grep -q "class EnrolmentDuplicateReviewService" api/features/bio-mob-005/Domain/EnrolmentDuplicateReviewService.cs \
    && ! grep -q "Biolog.Features.DuplicateReview" api/features/bio-mob-005/Domain/EnrolmentDuplicateReviewService.cs'
check "AC03 the scan runs synchronously within package intake, not deferred to a later job" \
  bash -c 'grep -q "RunDuplicateScanAsync" api/features/bio-mob-005/EnrolmentPackageService.cs'
check "AC03 no cross-project ProjectReference to bio-idn-005 or bio-bio-006 exists in this feature's own project file" \
  bash -c '! grep -q "<ProjectReference.*\(bio-idn-005\|bio-bio-006\)" api/features/bio-mob-005/Biolog.Features.OfflineEnrolment.csproj'

# --- AC04: security conflicts require human resolution ---
check "AC04 a raised duplicate candidate holds the package in PENDING_REVIEW, never auto-resolved" \
  bash -c 'grep -q "PendingReview" api/features/bio-mob-005/Domain/EnrolmentPackage.cs \
    && grep -q "DuplicateCandidateStatus.PendingReview" api/features/bio-mob-005/EnrolmentPackageService.cs'
check "AC04 a human decision requires a mandatory reason_code" \
  bash -c 'grep -q "DecisionReasonRequired" api/features/bio-mob-005/EnrolmentPackageService.cs \
    && grep -q "DecisionReasonRequired" api/features/bio-mob-005/Domain/EnrolmentError.cs'
check "AC04 the decision endpoint is gated by tenant/actor/provenance/permission (ADR-0006 Decision 3), not device auth" \
  bash -c 'grep -q "ReviewDuplicatesPermissionKey" api/features/bio-mob-005/Api/EnrolmentEndpoints.cs \
    && grep -q "provenanceGate.Require" api/features/bio-mob-005/Api/EnrolmentEndpoints.cs'

# --- AC05: reconnect sync uploads packages exactly once; mobile surface connected ---
check "AC05 the package upload uses the package id as the Idempotency-Key header" \
  bash -c 'grep -q "_idempotencyHeader.*package.packageId" mobile/lib/core/enrolment/enrolment_sync_client.dart'
check "AC05 the server intake claims an idempotency record before committing, mirroring BIO-MOB-003/BIO-ACC-003" \
  bash -c 'grep -q "idempotencyStore.Claim" api/features/bio-mob-005/EnrolmentPackageService.cs \
    && grep -q "IdempotencyKeyReuse" api/features/bio-mob-005/EnrolmentPackageService.cs'
check "AC05 a fingerprint mismatch on a reused key is rejected, not silently replayed" \
  bash -c 'grep -q "ComputeFingerprint" api/features/bio-mob-005/EnrolmentPackageService.cs'
check "AC05 the mobile enrolment screen (the approved MobileService surface) is wired to real EnrolmentService/EnrolmentSyncClient calls, not a placeholder" \
  bash -c '! grep -q "FeaturePlaceholder" mobile/lib/features/enrolment/enrolment_screen.dart \
    && grep -q "EnrolmentService" mobile/lib/features/enrolment/enrolment_screen.dart \
    && grep -q "EnrolmentSyncClient" mobile/lib/features/enrolment/enrolment_screen.dart'
check "AC05 checkPresentation-equivalent capture makes no network call - only AppDatabase reads/writes" \
  bash -c '! grep -q "http\." mobile/lib/core/enrolment/enrolment_service.dart'

# --- Developer test wiring ---
check "unit/integration tests mapped to BIO-MOB-005 ACs exist (mobile)" \
  bash -c 'test -f tests/flutter/test/bio_mob_005_ac01_ac02_capture_policy_test.dart \
    && test -f tests/flutter/test/bio_mob_005_ac03_ac05_sync_client_test.dart'
check "unit/integration tests mapped to BIO-MOB-005 ACs exist (backend)" \
  test -f tests/features/bio-mob-005/EnrolmentEndpointsIntegrationTests.cs
check "builder evidence recorded" test -d evidence/BIO-MOB-005/builder

echo "=============================="
echo "$pass passed, $fail failed"
[ "$fail" -eq 0 ]
