#!/usr/bin/env bash
# Offline-safe structural checks for BIO-PHO-001 (immutable photo master and derivatives).
# Behavioural assertions (append/versioning, derivative regeneration, assignment CAS, audit,
# demo-dataset determinism, HTTP pipeline) live in the executable xunit suites under
# tests/features/bio-pho-001/ and tests/dotnet/Biolog.IntegrationTests/PhotographyHostChannelTests.cs
# (see docs/features/bio-pho-001/README.md).
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"
pass=0; fail=0
check() { local label="$1"; shift; if "$@"; then echo "PASS  $label"; pass=$((pass+1)); else echo "FAIL  $label"; fail=$((fail+1)); fi; }

FEATURE=api/features/bio-pho-001
TESTS=tests/features/bio-pho-001

# --- AC01: PhotoMaster is immutable/versioned ---
check "AC01 feature project exists" test -f "$FEATURE/Biolog.Features.Photography.csproj"
check "AC01 project targets the pinned .NET SDK's TFM" bash -c "grep -q '<TargetFramework>net10.0</TargetFramework>' '$FEATURE/Biolog.Features.Photography.csproj'"
check "AC01 PhotoMaster carries no init/set accessor anywhere (structural immutability)" \
  bash -c "! grep -Eq 'init;|set;' $FEATURE/Domain/PhotoMaster.cs"
check "AC01 repository has no update/replace/delete method for a master" \
  bash -c "! grep -Eq 'Task.*(Update|Replace|Delete)Async' $FEATURE/Domain/IPhotoMasterRepository.cs"
check "AC01 module lives one-way in api/Biolog.Api/Modules, not back in the feature project" \
  bash -c "test -f api/Biolog.Api/Modules/BioPho001Module.cs && ! test -f '$FEATURE/BioPho001Module.cs'"
check "AC01 registered in the packaged Program.cs module array" \
  bash -c "grep -q 'new BioPho001Module()' api/Biolog.Api/Program.cs"
check "AC01 an append endpoint exists" \
  bash -c "grep -q 'photo-masters' $FEATURE/Api/PhotoEndpoints.cs"
check "AC01 a test proves the second append creates version 2 and leaves version 1 unchanged" \
  bash -c "grep -q 'AC01_a_second_append_for_the_same_person_creates_version_2_and_leaves_version_1_untouched' $TESTS/PhotoMasterServiceTests.cs"
check "AC01 a test proves concurrent appends for the same person never collide" \
  bash -c "grep -q 'AC01_appending_10_times_concurrently_for_the_same_person_never_produces_a_duplicate_or_skipped_version' $TESTS/PhotoMasterServiceTests.cs"
check "BIO-PHO-001-QA-01 PhotoMaster.Content defensively copies on construction and on every read" \
  bash -c "grep -q '_content.Clone()' $FEATURE/Domain/PhotoMaster.cs && grep -c 'Clone()' $FEATURE/Domain/PhotoMaster.cs | grep -q '^2$'"
check "BIO-PHO-001-QA-01 PhotoDerivative.Content defensively copies on construction and on every read" \
  bash -c "grep -q '_content.Clone()' $FEATURE/Domain/PhotoDerivative.cs && grep -c 'Clone()' $FEATURE/Domain/PhotoDerivative.cs | grep -q '^2$'"
check "BIO-PHO-001-QA-01 regression tests exist proving both tamper channels are closed" \
  bash -c "grep -q 'QA01_mutating_the_array_the_caller_originally_passed_in_never_changes_the_stored_content' $TESTS/PhotoMasterTests.cs &&
           grep -q 'QA01_mutating_a_previously_read_copy_never_changes_what_a_later_read_returns' $TESTS/PhotoMasterTests.cs &&
           grep -q 'QA01_mutating_the_array_the_caller_originally_passed_in_never_changes_the_stored_content' $TESTS/PhotoDerivativeTests.cs &&
           grep -q 'QA01_mutating_a_previously_read_copy_never_changes_what_a_later_read_returns' $TESTS/PhotoDerivativeTests.cs"

# --- AC02: print and display derivatives reference the master ---
check "AC02 derivative kind covers print and display" \
  bash -c "grep -q 'Print' $FEATURE/Domain/DerivativeKind.cs && grep -q 'Display' $FEATURE/Domain/DerivativeKind.cs"
check "AC02 PhotoDerivative carries a MasterVersion pointer back to the master" \
  bash -c "grep -q 'MasterVersion' $FEATURE/Domain/PhotoDerivative.cs"
check "AC02 a test proves a regenerated derivative references its master's version" \
  bash -c "grep -q 'AC02_a_regenerated_derivative_references_its_masters_version' $TESTS/PhotoDerivativeServiceTests.cs"

# --- AC03: derivative regeneration does not replace the master ---
check "AC03 the derivative repository has no dependency on the master repository's write side" \
  bash -c "! grep -v '^\s*///' $FEATURE/Domain/IPhotoDerivativeRepository.cs | grep -q 'IPhotoMasterRepository'"
check "AC03 a test proves many regenerations never change the master's content or version" \
  bash -c "grep -q 'AC03_regenerating_a_derivative_many_times_never_changes_the_masters_own_content_or_version' $TESTS/PhotoDerivativeServiceTests.cs"
check "AC03 a test proves regeneration replaces only the same-kind derivative, not a new one" \
  bash -c "grep -q 'AC03_regenerating_replaces_only_the_previous_derivative_of_the_same_kind' $TESTS/PhotoDerivativeServiceTests.cs"

# --- AC04: person-photo assignment is versioned and audited ---
check "AC04 PhotoAssignment carries a real CAS version field" \
  bash -c "grep -q 'required int Version' $FEATURE/Domain/PhotoAssignment.cs"
check "AC04 the assignment repository exposes a compare-and-set replace" \
  bash -c "grep -q 'ReplaceAsync(PhotoAssignment expectedCurrent' $FEATURE/Domain/IPhotoAssignmentRepository.cs"
check "AC04 every mutation is audited before commit (audit-atomicity pattern)" \
  bash -c "grep -q 'AuditAsync' $FEATURE/PhotoAssignmentService.cs && grep -q 'AuditAsync' $FEATURE/PhotoMasterService.cs"
check "AC04 a stale expected_version is rejected with concurrency_conflict" \
  bash -c "grep -q 'Reassigning_with_a_stale_expected_version_is_rejected_with_concurrency_conflict' $TESTS/PhotoAssignmentServiceTests.cs"
check "AC04 a missing expected_version on an existing assignment fails closed" \
  bash -c "grep -q 'precondition_required' $FEATURE/Domain/PhotoError.cs"
check "AC04 a test proves every reassignment is audited with previous and new master version" \
  bash -c "grep -q 'AC04_every_reassignment_is_audited_with_the_previous_and_new_master_version' $TESTS/PhotoAssignmentServiceTests.cs"
check "AC04 audit detail never carries the raw photo content (ADR-0003: photo reference is personal, never carried as bytes)" \
  bash -c "! grep -v '^\s*///' $FEATURE/Audit/PhotoAuditEventBuilder.cs | grep -q 'Content'"

# --- AC05: synthetic demo photos remain consistently assigned in tests ---
check "AC05 demo content generation is deterministic, never Random or DateTime" \
  bash -c "! grep -Eq 'new Random\(|DateTime.Now' $TESTS/DemoPhotoCatalog.cs"
check "AC05 a test proves the same person id always yields byte-identical synthetic content" \
  bash -c "grep -q 'AC05_the_same_person_id_always_produces_byte_identical_synthetic_content' $TESTS/DemoPhotoCatalogTests.cs"
check "AC05 a test proves repeated seeding never creates a second master or changes the assignment" \
  bash -c "grep -q 'AC05_seeding_the_same_person_repeatedly_never_creates_a_second_master_version_or_changes_the_assignment' $TESTS/DemoPhotoCatalogTests.cs"

# --- Cross-tenant / dependency wiring ---
check "cross-feature person existence is proven through this project's own narrow gateway, not a ProjectReference to bio-idn-001" \
  bash -c "! grep -q '<ProjectReference' $FEATURE/Biolog.Features.Photography.csproj"
check "no image-processing library dependency was added (disclosed scope boundary)" \
  bash -c "! grep -Eiq 'SixLabors|SkiaSharp|System.Drawing' $FEATURE/Biolog.Features.Photography.csproj"

# --- Developer test wiring ---
check "unit tests exist for raster inspection/master/derivative/assignment/demo-catalogue" bash -c \
  'test -f tests/features/bio-pho-001/PhotoRasterInspectorTests.cs &&
   test -f tests/features/bio-pho-001/PhotoMasterTests.cs &&
   test -f tests/features/bio-pho-001/PhotoDerivativeTests.cs &&
   test -f tests/features/bio-pho-001/PhotoMasterServiceTests.cs &&
   test -f tests/features/bio-pho-001/PhotoDerivativeServiceTests.cs &&
   test -f tests/features/bio-pho-001/PhotoAssignmentServiceTests.cs &&
   test -f tests/features/bio-pho-001/DemoPhotoCatalogTests.cs'
check "integration tests exercise the real HTTP endpoints (self-hosted)" test -f tests/features/bio-pho-001/PhotoEndpointsIntegrationTests.cs
check "a real packaged-host channel test exists (not merely an injected test double)" \
  test -f tests/dotnet/Biolog.IntegrationTests/PhotographyHostChannelTests.cs
check "all fixtures marked SYNTHETIC" bash -c \
  'grep -rq SYNTHETIC tests/features/bio-pho-001 tests/dotnet/Biolog.IntegrationTests/PhotographyHostChannelTests.cs'
check "builder evidence recorded" test -d evidence/BIO-PHO-001/builder

echo "=============================="
echo "$pass passed, $fail failed"
[ "$fail" -eq 0 ]
