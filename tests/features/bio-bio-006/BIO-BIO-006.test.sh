#!/usr/bin/env bash
# Offline-safe structural checks for BIO-BIO-006 (1:N fingerprint duplicate/identification
# search). Behavioural assertions live in the executable xunit suite under
# tests/features/bio-bio-006/ (see docs/features/bio-bio-006/README.md).
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"
pass=0; fail=0
check() { local label="$1"; shift; if "$@"; then echo "PASS  $label"; pass=$((pass+1)); else echo "FAIL  $label"; fail=$((fail+1)); fi; }

FEATURE=api/features/bio-bio-006/Biolog.Features.FingerprintSearch

# --- AC01: search returns ranked candidates and scores ---
check "AC01 feature project exists" test -f "$FEATURE/Biolog.Features.FingerprintSearch.csproj"
check "AC01 project targets the pinned .NET SDK's TFM" bash -c "grep -q '<TargetFramework>net10.0</TargetFramework>' '$FEATURE/Biolog.Features.FingerprintSearch.csproj'"
check "AC01 a ranked search result type exists carrying candidates and scores" \
  bash -c "grep -q 'IReadOnlyList<FingerprintSearchCandidate> RankedCandidates' '$FEATURE/Domain/FingerprintSearchResult.cs' && grep -q 'double Score' '$FEATURE/Domain/FingerprintSearchCandidate.cs'"
check "AC01 candidates are ordered by score descending and capped at the caller-requested count" \
  bash -c "grep -q 'OrderByDescending(s => s.Score)' '$FEATURE/Service/FingerprintSearchService.cs' && grep -q '.Take(maxCandidates)' '$FEATURE/Service/FingerprintSearchService.cs'"
check "AC01 a structural test proves ranking order" \
  bash -c "grep -q 'AC01_ranked_candidates_are_ordered_by_score_descending' tests/features/bio-bio-006/FingerprintSearchServiceTests.cs"

# --- AC02: permissions distinguish 1:N from 1:1 ---
check "AC02 a permission gateway contract exists, separate from the service" test -f "$FEATURE/Domain/IBiometricSearchPermissionGateway.cs"
check "AC02 a distinct 1:N search permission key is defined and enforced before the gallery is read" \
  bash -c "grep -q 'SearchPermissionKey = \"biometric.fingerprint_search_1n\"' '$FEATURE/Service/FingerprintSearchService.cs' && grep -q 'permissionGateway.HasPermissionAsync' '$FEATURE/Service/FingerprintSearchService.cs'"
check "AC02 a structural test proves search is rejected without the 1:N permission" \
  bash -c "grep -q 'AC02_search_without_the_1n_permission_is_rejected' tests/features/bio-bio-006/FingerprintSearchServiceTests.cs"
check "AC02 a structural test proves an unrelated permission does not authorise 1:N search" \
  bash -c "grep -q 'AC02_holding_an_unrelated_permission_key_does_not_authorise_1n_search' tests/features/bio-bio-006/FingerprintSearchServiceTests.cs"

# --- AC03: possible duplicate routes to human review ---
check "AC03 a review-queue entry type exists, distinct from a search candidate" test -f "$FEATURE/Domain/FingerprintDuplicateReviewEntry.cs"
check "AC03 a review repository persistence seam exists" test -f "$FEATURE/Domain/IFingerprintDuplicateReviewRepository.cs"
check "AC03 a possible-duplicate candidate is raised/augmented onto the review queue" \
  bash -c "grep -q 'RaiseOrAugmentReviewAsync' '$FEATURE/Service/FingerprintSearchService.cs'"
check "AC03 a decided pair is never reopened by a later search" \
  bash -c "grep -q 'A human already reviewed this pair' '$FEATURE/Service/FingerprintSearchService.cs'"
check "AC03 a human decide operation exists" \
  bash -c "grep -q 'public async Task<FingerprintDuplicateReviewEntry> DecideAsync' '$FEATURE/Service/FingerprintSearchService.cs'"
check "AC03 a structural test proves a possible duplicate raises a pending review entry" \
  bash -c "grep -q 'AC03_a_score_at_or_above_the_threshold_raises_a_pending_review_entry' tests/features/bio-bio-006/FingerprintSearchServiceTests.cs"
check "BIO-BIO-006-QA-01 fix: review routing is computed from every scored candidate, not the capped ranked list" \
  bash -c "grep -q 'foreach (var (personId, score) in scored)' '$FEATURE/Service/FingerprintSearchService.cs'"
check "BIO-BIO-006-QA-01 regression test proves an above-threshold candidate crowded out of the cap still routes to review" \
  bash -c "grep -q 'AC03_every_above_threshold_candidate_routes_to_review_even_when_crowded_out_of_the_capped_ranked_list' tests/features/bio-bio-006/FingerprintSearchServiceTests.cs"
check "AC03 a structural test proves a decided pair is never reopened" \
  bash -c "grep -q 'AC03_a_decided_pair_is_never_reopened_by_a_later_search' tests/features/bio-bio-006/FingerprintSearchServiceTests.cs"
check "BIO-BIO-006-QA-02 fix: raise/augment and decide are both serialized per pair via a dedicated lock registry" \
  bash -c "test -f '$FEATURE/Domain/FingerprintDuplicateReviewLockRegistry.cs' && grep -q 'lockRegistry.AcquireAsync' '$FEATURE/Service/FingerprintSearchService.cs'"
check "BIO-BIO-006-QA-02 regression tests prove concurrent raises for the same new pair converge and the lock mechanism itself is mutually exclusive per pair" \
  bash -c "grep -q 'AC03_two_simultaneous_searches_raising_the_same_new_pair_both_converge_without_throwing' tests/features/bio-bio-006/FingerprintSearchServiceTests.cs && grep -q 'A_second_acquire_for_the_same_pair_waits_until_the_first_is_released' tests/features/bio-bio-006/FingerprintDuplicateReviewLockRegistryTests.cs"

# --- AC04: no automatic PERSON merge occurs ---
check "AC04 no ProjectReference to BIO-IDN-001 exists" \
  bash -c "! grep -q '<ProjectReference[^>]*bio-idn-001' '$FEATURE/Biolog.Features.FingerprintSearch.csproj'"
check "AC04 the review status enum has no Merged member" \
  bash -c "! grep -qi 'Merged,' '$FEATURE/Domain/FingerprintDuplicateReviewStatus.cs'"
check "AC04 a structural test proves this assembly has no reference to BIO-IDN-001's own assembly" \
  bash -c "grep -q 'AC04_this_assembly_has_no_reference_to_BIO_IDN_001s_own_assembly' tests/features/bio-bio-006/FingerprintSearchServiceTests.cs"

# --- AC05: search scope is tenant constrained ---
check "AC05 the gallery store is keyed by tenant and never returns another tenant's templates" \
  bash -c "grep -q 'GetAllForTenantAsync' '$FEATURE/Domain/IFingerprintGalleryStore.cs'"
check "AC05 SearchAsync accepts no parameter that would widen a search beyond the resolved tenant" \
  bash -c "! grep -E 'SearchAsync\(' -A6 '$FEATURE/Service/FingerprintSearchService.cs' | grep -qi 'crossTenant\|allTenants\|includeOtherTenants'"
check "AC05 a structural test proves cross-tenant gallery isolation" \
  bash -c "grep -q 'AC05_a_gallery_entry_indexed_for_one_tenant_is_invisible_to_another_tenants_search' tests/features/bio-bio-006/FingerprintSearchServiceTests.cs"
check "AC05 a structural test proves cross-tenant review-queue isolation" \
  bash -c "grep -q 'AC05_review_queue_never_leaks_another_tenants_entries' tests/features/bio-bio-006/FingerprintSearchServiceTests.cs"

# --- Governance (BIO-IDN-001-QA-01 shape) ---
check "governance: the gallery store and its implementation are internal, not public" \
  bash -c "grep -q '^internal interface IFingerprintGalleryStore' '$FEATURE/Domain/IFingerprintGalleryStore.cs' && grep -q '^internal sealed class InMemoryFingerprintGalleryStore' '$FEATURE/Domain/InMemoryFingerprintGalleryStore.cs'"
check "governance: the review repository and its implementation are internal, not public" \
  bash -c "grep -q '^internal interface IFingerprintDuplicateReviewRepository' '$FEATURE/Domain/IFingerprintDuplicateReviewRepository.cs' && grep -q '^internal sealed class InMemoryFingerprintDuplicateReviewRepository' '$FEATURE/Domain/InMemoryFingerprintDuplicateReviewRepository.cs'"
check "governance: the service is internal too" \
  bash -c "grep -q '^internal sealed class FingerprintSearchService' '$FEATURE/Service/FingerprintSearchService.cs'"
check "governance: the InternalsVisibleTo seam is explicit and auditable in source control" \
  bash -c "grep -q '<InternalsVisibleTo Include=\"Biolog.Api\" />' '$FEATURE/Biolog.Features.FingerprintSearch.csproj' && grep -q '<InternalsVisibleTo Include=\"Biolog.Features.FingerprintSearch.Tests\" />' '$FEATURE/Biolog.Features.FingerprintSearch.csproj'"
check "governance: reflection tests prove the gallery/repository/service are inaccessible to ordinary production consumers" \
  bash -c "grep -q 'IFingerprintGalleryStore_and_InMemory_implementation_are_not_public' tests/features/bio-bio-006/FingerprintSearchGovernanceTests.cs"
check "governance: cross-tenant review lookup returns null, not an exception (no enumeration oracle)" \
  bash -c "grep -q 'Cross-tenant lookup returns null' '$FEATURE/Domain/InMemoryFingerprintDuplicateReviewRepository.cs'"

# --- Feature-to-feature dependency shape ---
check "this project references BIO-BIO-001's provider-neutral biometric library" \
  bash -c "grep -q 'Biolog.Features.Biometrics.csproj' '$FEATURE/Biolog.Features.FingerprintSearch.csproj'"
check "no ProjectReference to BIO-BIO-002, BIO-IDN-005 or BIO-IDN-006 (all reached via this story's own gateway/independent design)" \
  bash -c "! grep -q '<ProjectReference[^>]*bio-bio-002' '$FEATURE/Biolog.Features.FingerprintSearch.csproj' && ! grep -q '<ProjectReference[^>]*bio-idn-005' '$FEATURE/Biolog.Features.FingerprintSearch.csproj' && ! grep -q '<ProjectReference[^>]*bio-idn-006' '$FEATURE/Biolog.Features.FingerprintSearch.csproj'"
check "module project references stay one-way (no ProjectReference back to Biolog.Api)" \
  bash -c "! grep -q '<ProjectReference[^>]*Biolog\.Api\.csproj' '$FEATURE/Biolog.Features.FingerprintSearch.csproj'"

# --- Module wiring ---
check "module lives one-way in api/Biolog.Api/Modules, not back in the feature project" \
  bash -c "test -f api/Biolog.Api/Modules/BioBio006Module.cs && ! test -f '$FEATURE/BioBio006Module.cs'"
check "registered in the packaged Program.cs module array" \
  bash -c "grep -q 'new BioBio006Module()' api/Biolog.Api/Program.cs"
check "no HTTP endpoints are mapped (AC01-AC05 are about the search/review model, matching every prior BIO-BIO story's deferred-integration call)" \
  bash -c "! grep -q 'endpoints\.Map' api/Biolog.Api/Modules/BioBio006Module.cs"
check "the permission gateway adapter wraps BIO-IDN-006's own already-registered PermissionService" \
  bash -c "grep -q 'PermissionService.ActorHasPermissionAsync\|permissionService.ActorHasPermissionAsync' api/Biolog.Api/Modules/BioBio006Module.cs"

# --- Developer test wiring ---
check "unit tests reference the feature project" \
  bash -c "grep -q 'Biolog.Features.FingerprintSearch.csproj' tests/features/bio-bio-006/Biolog.Features.FingerprintSearch.Tests.csproj"
check "docs/features/bio-bio-006/README.md documents the feature" test -f docs/features/bio-bio-006/README.md
check "builder evidence recorded" test -d evidence/features/bio-bio-006/builder

echo "=============================="
echo "$pass passed, $fail failed"
[ "$fail" -eq 0 ]
