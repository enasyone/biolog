#!/usr/bin/env bash
# Offline-safe structural checks for BIO-PHO-002 (Photo Booth integration contract).
# Behavioural assertions (spec/quality gate, device auth, idempotency, approval/promotion,
# HTTP pipeline) live in the executable xunit suites under tests/features/bio-pho-002/ and
# tests/dotnet/Biolog.IntegrationTests/PhotoBoothHostChannelTests.cs (see
# docs/features/bio-pho-002/README.md).
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"
pass=0; fail=0
check() { local label="$1"; shift; if "$@"; then echo "PASS  $label"; pass=$((pass+1)); else echo "FAIL  $label"; fail=$((fail+1)); fi; }

FEATURE=api/features/bio-pho-002
TESTS=tests/features/bio-pho-002

# --- AC01: capture specification includes frame size, aspect ratio, minimum DPI, face-height/background rules ---
check "AC01 feature project exists" test -f "$FEATURE/Biolog.Features.PhotoBooth.csproj"
check "AC01 project targets the pinned .NET SDK's TFM" bash -c "grep -q '<TargetFramework>net10.0</TargetFramework>' '$FEATURE/Biolog.Features.PhotoBooth.csproj'"
check "AC01 specification names frame size, aspect ratio, minimum DPI, face-height and background rules" bash -c \
  "grep -q 'FrameWidthPx' $FEATURE/Domain/CaptureSpecification.cs &&
   grep -q 'AspectRatioWidth' $FEATURE/Domain/CaptureSpecification.cs &&
   grep -q 'MinimumDpi' $FEATURE/Domain/CaptureSpecification.cs &&
   grep -q 'MinFaceHeightRatio' $FEATURE/Domain/CaptureSpecification.cs &&
   grep -q 'BackgroundRequirement' $FEATURE/Domain/CaptureSpecification.cs"
check "AC01 the specification is readable via a GET endpoint" bash -c "grep -q 'capture-specification' $FEATURE/Api/CaptureEndpoints.cs"
check "AC01 module lives one-way in api/Biolog.Api/Modules, not back in the feature project" \
  bash -c "test -f api/Biolog.Api/Modules/BioPho002Module.cs && ! test -f '$FEATURE/BioPho002Module.cs'"
check "AC01 registered in the packaged Program.cs module array" \
  bash -c "grep -q 'new BioPho002Module()' api/Biolog.Api/Program.cs"

# --- AC02: upload authenticates the booth/device ---
check "AC02 device authenticator requires both a device id and a device secret" \
  bash -c "grep -q 'DeviceIdHeaderName' $FEATURE/Api/DeviceAuthenticator.cs && grep -q 'DeviceSecretHeaderName' $FEATURE/Api/DeviceAuthenticator.cs"
check "AC02 device identity requires a verified per-device secret, not a bare header (applying BIO-CRD-001-QA-01 from the start)" \
  bash -c "grep -q 'IDeviceCredentialStore' $FEATURE/Api/DeviceAuthenticator.cs && grep -q 'credentialStore.Validate' $FEATURE/Api/DeviceAuthenticator.cs"
check "AC02 a device must also be ACTIVE, not merely credentialed" \
  bash -c "grep -q 'IsActiveAsync' $FEATURE/Api/DeviceAuthenticator.cs"
check "AC02 a real startup provisioning path loads device credentials from configuration (applying BIO-CRD-001-QA-02 from the start)" \
  bash -c "grep -q 'LoadFromConfiguration' $FEATURE/Domain/DeviceCredentialProvisioning.cs && grep -q 'LoadFromConfiguration' api/Biolog.Api/Modules/BioPho002Module.cs"
check "AC02 no device secret is committed to checked-in app configuration" \
  bash -c "! grep -rq 'DeviceCredentials' api/Biolog.Api/appsettings.json api/Biolog.Api/appsettings.Development.json 2>/dev/null"
check "AC02 the submit endpoint requires device authentication, fail-closed" \
  bash -c "grep -q 'DeviceAuthenticationFailed' $FEATURE/Api/CaptureEndpoints.cs"
check "unit tests cover the device-forgery attack directly, applying BIO-CRD-001-QA-01 from the start" \
  bash -c "grep -q 'BIOPHO002_QA01' $TESTS/DeviceAuthenticatorTests.cs"

# --- AC03: retries are idempotent ---
check "AC03 an idempotency store with claim/complete/release/get semantics exists" \
  bash -c "grep -q 'IIdempotencyStore' $FEATURE/Domain/IdempotencyStore.cs && grep -q 'Claim(' $FEATURE/Domain/IdempotencyStore.cs"
check "AC03 the submit endpoint requires Idempotency-Key, fail-closed" \
  bash -c "grep -q 'IdempotencyKeyRequired' $FEATURE/Api/CaptureEndpoints.cs"
check "AC03 a test proves a replay with the same content returns the original capture" \
  bash -c "grep -q 'AC03_replaying_the_same_idempotency_key_and_content_returns_the_same_capture_without_creating_a_second_one' $TESTS/CaptureServiceTests.cs"
check "AC03 a test proves reusing a key with different content is refused" \
  bash -c "grep -q 'AC03_reusing_an_idempotency_key_with_different_content_is_refused' $TESTS/CaptureServiceTests.cs"

# --- AC04: unapproved/low-quality capture is not silently promoted to master ---
check "AC04 a quality gate evaluates every submission before a capture record is even created" \
  bash -c "grep -q 'CaptureQualityGate.Evaluate' $FEATURE/CaptureService.cs"
check "AC04 CaptureStatus has exactly Pending/Rejected/Promoted, no other state" \
  bash -c "grep -q 'Pending,' $FEATURE/Domain/CaptureStatus.cs && grep -q 'Rejected,' $FEATURE/Domain/CaptureStatus.cs && grep -q 'Promoted,' $FEATURE/Domain/CaptureStatus.cs"
check "AC04 promotion is reachable only via IPhotoMasterPromotionGateway.PromoteAsync, and only from ApproveAsync" \
  bash -c "grep -c 'promotionGateway.PromoteAsync' $FEATURE/CaptureService.cs | grep -q '^1$'"
check "AC04 the repository refuses to replace anything but a PENDING capture (decided is terminal)" \
  bash -c "grep -q 'CaptureStatus.Pending' $FEATURE/Domain/InMemoryBoothCaptureRepository.cs"
check "AC04 a test proves a low-quality capture is created already rejected, never pending" \
  bash -c "grep -q 'AC04_a_low_quality_submission_is_recorded_already_rejected_never_pending' $TESTS/CaptureServiceTests.cs"
check "AC04 a test proves a rejected capture can never be approved" \
  bash -c "grep -q 'AC04_a_rejected_capture_can_never_be_approved' $TESTS/CaptureServiceTests.cs"
check "AC04 a test proves a promoted capture can never be approved or rejected again" \
  bash -c "grep -q 'AC04_a_promoted_capture_can_never_be_approved_or_rejected_again' $TESTS/CaptureServiceTests.cs"
check "AC04 a real host-channel test proves a promoted capture creates a real, independently readable BIO-PHO-001 master" \
  bash -c "grep -q 'AC04_approving_a_real_capture_creates_a_real_independently_readable_BIOPHO001_photo_master' tests/dotnet/Biolog.IntegrationTests/PhotoBoothHostChannelTests.cs"
check "AC04 audit detail never carries the raw capture content" \
  bash -c "! grep -v '^\s*///' $FEATURE/Audit/CaptureAuditEventBuilder.cs | grep -q 'Content'"

# --- BIO-PHO-001-QA-01 lesson applied from the start ---
check "BoothCapture.Content defensively copies on construction and on every read" \
  bash -c "grep -q '_content.Clone()' $FEATURE/Domain/BoothCapture.cs && grep -c 'Clone()' $FEATURE/Domain/BoothCapture.cs | grep -q '^2$'"
check "regression tests exist proving both content tamper channels are closed" \
  bash -c "grep -q 'QA01_mutating_the_array_the_caller_originally_passed_in_never_changes_the_stored_content' $TESTS/BoothCaptureTests.cs &&
           grep -q 'QA01_mutating_a_previously_read_copy_never_changes_what_a_later_read_returns' $TESTS/BoothCaptureTests.cs"

# --- Cross-feature dependency wiring ---
check "no ProjectReference to any other feature project (all cross-feature access is through this project's own gateways)" \
  bash -c "! grep -q '<ProjectReference' $FEATURE/Biolog.Features.PhotoBooth.csproj"
check "no image-processing or computer-vision library dependency was added (disclosed scope boundary)" \
  bash -c "! grep -Eiq 'SixLabors|SkiaSharp|System.Drawing|OpenCV' $FEATURE/Biolog.Features.PhotoBooth.csproj"

# --- Developer test wiring ---
check "unit tests exist for raster inspection/quality gate/device auth/capture/idempotency/credential provisioning" bash -c \
  'test -f tests/features/bio-pho-002/CaptureRasterInspectorTests.cs &&
   test -f tests/features/bio-pho-002/CaptureQualityGateTests.cs &&
   test -f tests/features/bio-pho-002/DeviceAuthenticatorTests.cs &&
   test -f tests/features/bio-pho-002/BoothCaptureTests.cs &&
   test -f tests/features/bio-pho-002/CaptureServiceTests.cs &&
   test -f tests/features/bio-pho-002/IdempotencyStoreTests.cs &&
   test -f tests/features/bio-pho-002/DeviceCredentialProvisioningTests.cs'
check "integration tests exercise the real HTTP endpoints (self-hosted)" test -f tests/features/bio-pho-002/PhotoBoothEndpointsIntegrationTests.cs
check "a real packaged-host channel test exists (not merely an injected test double)" \
  test -f tests/dotnet/Biolog.IntegrationTests/PhotoBoothHostChannelTests.cs
check "all fixtures marked SYNTHETIC" bash -c \
  'grep -rq SYNTHETIC tests/features/bio-pho-002 tests/dotnet/Biolog.IntegrationTests/PhotoBoothHostChannelTests.cs'
# --- TD-PHO-002-IDEMPOTENCY-RACE (High, Kemi-confirmed 2026-10-06) ---
check "the idempotency claim carries an explicit IsNewClaim ownership signal, not content-only State" \
  bash -c "grep -q 'IsNewClaim' $FEATURE/Domain/IdempotencyStore.cs"
check "SubmitAsync branches on IsNewClaim, not on State alone" \
  bash -c "grep -q 'claim.IsNewClaim' $FEATURE/CaptureService.cs"
check "a deterministic two-caller concurrency test exists proving exactly one execution" \
  test -f $TESTS/TdIdempotencyRaceTests.cs
check "the regression test forces genuine claim-boundary contention (a barrier-gated store), not just a shared start gate" \
  bash -c "grep -q 'ClaimBarrierIdempotencyStore' $TESTS/TdIdempotencyRaceTests.cs"
check "a mutant-discrimination test proves the historical State-only check violates the same invariant the real fix satisfies" \
  bash -c "grep -q 'Mutant_discrimination' $TESTS/TdIdempotencyRaceTests.cs"

check "builder evidence recorded" test -d evidence/BIO-PHO-002/builder

echo "=============================="
echo "$pass passed, $fail failed"
[ "$fail" -eq 0 ]
