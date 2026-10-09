#!/usr/bin/env bash
# Offline-safe structural checks for BIO-BIO-007 (1:N face identification/duplicate search).
# Behavioural assertions live in the executable xunit suite under tests/features/bio-bio-007/
# (see docs/features/bio-bio-007/README.md).
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"
pass=0; fail=0
check() { local label="$1"; shift; if "$@"; then echo "PASS  $label"; pass=$((pass+1)); else echo "FAIL  $label"; fail=$((fail+1)); fi; }

FEATURE=api/features/bio-bio-007/Biolog.Features.FaceSearch

# --- AC01: search returns ranked candidates with similarity ---
check "AC01 feature project exists" test -f "$FEATURE/Biolog.Features.FaceSearch.csproj"
check "AC01 project targets the pinned .NET SDK's TFM" bash -c "grep -q '<TargetFramework>net10.0</TargetFramework>' '$FEATURE/Biolog.Features.FaceSearch.csproj'"
check "AC01 a ranked search result type exists carrying candidates and similarity" \
  bash -c "grep -q 'IReadOnlyList<FaceSearchCandidate> RankedCandidates' '$FEATURE/Domain/FaceSearchResult.cs' && grep -q 'double Score' '$FEATURE/Domain/FaceSearchCandidate.cs'"
check "AC01 candidates are ordered by score descending and capped at the caller-requested count" \
  bash -c "grep -q 'OrderByDescending(s => s.Score)' '$FEATURE/Service/FaceSearchService.cs' && grep -q '.Take(maxCandidates)' '$FEATURE/Service/FaceSearchService.cs'"
check "AC01 a structural test proves ranking order" \
  bash -c "grep -q 'AC01_ranked_candidates_are_ordered_by_score_descending' tests/features/bio-bio-007/FaceSearchServiceTests.cs"

# --- BIO-BIO-007-QA-01 (Chioma acceptance, High): only approved references enter the gallery ---
check "QA-01 a narrow approval gateway exists, not a call into BIO-BIO-003's internal service" \
  bash -c "test -f '$FEATURE/Domain/IFaceGalleryApprovalGateway.cs' && ! grep -v '^\s*///' '$FEATURE/Service/FaceSearchService.cs' | grep -q 'FaceEnrolmentService'"
check "QA-01 IndexAsync takes no raw BiometricSample parameter" \
  bash -c "! grep -E 'public async Task IndexAsync\(' '$FEATURE/Service/FaceSearchService.cs' | grep -q 'BiometricSample'"
check "QA-01 IndexAsync resolves the approved reference through the gateway before indexing anything" \
  bash -c "grep -q 'approvalGateway.GetLatestApprovedReferenceAsync' '$FEATURE/Service/FaceSearchService.cs'"
check "QA-01 a missing approved reference is a precondition failure, not a routine outcome" \
  bash -c "grep -q 'FaceSearchException.NoApprovedReference' '$FEATURE/Service/FaceSearchService.cs'"
check "QA-01 regression tests prove no raw-sample overload exists and an unapproved person cannot be indexed" \
  bash -c "grep -q 'QA01_indexing_throws_when_the_person_has_no_approved_reference' tests/features/bio-bio-007/FaceSearchServiceTests.cs && grep -q 'QA01_IndexAsync_takes_no_raw_BiometricSample_parameter' tests/features/bio-bio-007/FaceSearchServiceTests.cs"

# --- AC02: 1:N requires explicit tenant feature enablement ---
check "AC02 a tenant feature-flag policy contract exists, separate from the service" test -f "$FEATURE/Domain/IFaceSearchFeatureFlagPolicy.cs"
check "AC02 the feature flag is checked before the gallery is read, fail-closed by default" \
  bash -c "grep -q 'featureFlagPolicy.IsEnabledForTenant(tenantId)' '$FEATURE/Service/FaceSearchService.cs' && grep -q 'TryGetValue(tenantId, out var enabled) && enabled' '$FEATURE/Domain/SyntheticFaceSearchFeatureFlagPolicy.cs'"
check "AC02 a structural test proves search is rejected for a tenant that never enabled the feature" \
  bash -c "grep -q 'AC02_search_is_rejected_for_a_tenant_that_never_explicitly_enabled_the_feature' tests/features/bio-bio-007/FaceSearchServiceTests.cs"
check "AC02 a structural test proves enabling one tenant does not enable another" \
  bash -c "grep -q 'AC02_enabling_one_tenant_does_not_enable_another' tests/features/bio-bio-007/FaceSearchServiceTests.cs"

# --- AC03: no automatic authorization is granted from search alone ---
check "AC03 the result and candidate types carry no decision/authorization field" \
  bash -c "! grep -v '^\s*///' '$FEATURE/Domain/FaceSearchResult.cs' | grep -qiE 'Decision|Authoriz' && ! grep -v '^\s*///' '$FEATURE/Domain/FaceSearchCandidate.cs' | grep -qiE 'Decision|Authoriz'"
check "AC03 the service exposes no decide/authorize method" \
  bash -c "! grep -v '^\s*///' '$FEATURE/Service/FaceSearchService.cs' | grep -E 'public .*(Decide|Authoriz)'"
check "AC03 a structural test proves no decision/authorization field or method exists" \
  bash -c "grep -q 'AC03_the_result_and_candidate_types_carry_no_authorization_or_decision_field' tests/features/bio-bio-007/FaceSearchServiceTests.cs && grep -q 'AC03_the_service_exposes_no_decide_or_authorize_method' tests/features/bio-bio-007/FaceSearchServiceTests.cs"

# --- AC04: no automatic PERSON merge occurs ---
check "AC04 no ProjectReference to BIO-IDN-001 exists" \
  bash -c "! grep -q '<ProjectReference[^>]*bio-idn-001' '$FEATURE/Biolog.Features.FaceSearch.csproj'"
check "AC04 a structural test proves this assembly has no reference to BIO-IDN-001's own assembly" \
  bash -c "grep -q 'AC04_this_assembly_has_no_reference_to_BIO_IDN_001s_own_assembly' tests/features/bio-bio-007/FaceSearchServiceTests.cs"

# --- AC05: population scope and audit are explicit ---
check "AC05 the gallery store is keyed by tenant and never returns another tenant's templates" \
  bash -c "grep -q 'GetAllForTenantAsync' '$FEATURE/Domain/IFaceGalleryStore.cs'"
check "AC05 the search result names the population scope and size explicitly" \
  bash -c "grep -q 'string PopulationScope' '$FEATURE/Domain/FaceSearchResult.cs' && grep -q 'int PopulationSize' '$FEATURE/Domain/FaceSearchResult.cs'"
check "AC05 an explicit audit sink/event exists and every search writes to it" \
  bash -c "test -f '$FEATURE/Audit/FaceSearchAuditEvent.cs' && test -f '$FEATURE/Audit/IFaceSearchAuditSink.cs' && grep -q 'AuditAsync(' '$FEATURE/Service/FaceSearchService.cs'"
check "AC05 an audit write that cannot be confirmed durable fails the search" \
  bash -c "grep -q 'FaceSearchException.AuditUnavailable' '$FEATURE/Service/FaceSearchService.cs'"
check "AC05 a structural test proves the population scope/size are recorded" \
  bash -c "grep -q 'AC05_the_result_names_the_population_scope_and_size_explicitly' tests/features/bio-bio-007/FaceSearchServiceTests.cs"
check "AC05 a structural test proves every search writes an audit event" \
  bash -c "grep -q 'AC05_every_search_writes_an_audit_event' tests/features/bio-bio-007/FaceSearchServiceTests.cs"
check "AC05 a structural test proves cross-tenant gallery isolation" \
  bash -c "grep -q 'AC05_a_gallery_entry_indexed_for_one_tenant_is_invisible_to_another_tenants_search' tests/features/bio-bio-007/FaceSearchServiceTests.cs"
check "AC05 a structural test proves cross-tenant audit isolation" \
  bash -c "grep -q 'AC05_audit_trail_never_leaks_another_tenants_events' tests/features/bio-bio-007/FaceSearchServiceTests.cs"

# --- Governance (BIO-IDN-001-QA-01 shape) ---
check "governance: the gallery store and its implementation are internal, not public" \
  bash -c "grep -q '^internal interface IFaceGalleryStore' '$FEATURE/Domain/IFaceGalleryStore.cs' && grep -q '^internal sealed class InMemoryFaceGalleryStore' '$FEATURE/Domain/InMemoryFaceGalleryStore.cs'"
check "governance: the audit sink and its implementation are internal, not public" \
  bash -c "grep -q '^internal interface IFaceSearchAuditSink' '$FEATURE/Audit/IFaceSearchAuditSink.cs' && grep -q '^internal sealed class InMemoryFaceSearchAuditSink' '$FEATURE/Audit/InMemoryFaceSearchAuditSink.cs'"
check "governance: the service is internal too" \
  bash -c "grep -q '^internal sealed class FaceSearchService' '$FEATURE/Service/FaceSearchService.cs'"
check "governance: the InternalsVisibleTo seam is explicit and auditable in source control" \
  bash -c "grep -q '<InternalsVisibleTo Include=\"Biolog.Api\" />' '$FEATURE/Biolog.Features.FaceSearch.csproj' && grep -q '<InternalsVisibleTo Include=\"Biolog.Features.FaceSearch.Tests\" />' '$FEATURE/Biolog.Features.FaceSearch.csproj'"
check "governance: reflection tests prove the gallery/audit/service are inaccessible to ordinary production consumers" \
  bash -c "grep -q 'IFaceGalleryStore_and_InMemory_implementation_are_not_public' tests/features/bio-bio-007/FaceSearchGovernanceTests.cs"

# --- Feature-to-feature dependency shape ---
check "this project references BIO-BIO-001's provider-neutral biometric library" \
  bash -c "grep -q 'Biolog.Features.Biometrics.csproj' '$FEATURE/Biolog.Features.FaceSearch.csproj'"
check "no ProjectReference to BIO-BIO-003 (reached via this story's own independent gallery design)" \
  bash -c "! grep -q '<ProjectReference[^>]*bio-bio-003' '$FEATURE/Biolog.Features.FaceSearch.csproj'"
check "module project references stay one-way (no ProjectReference back to Biolog.Api)" \
  bash -c "! grep -q '<ProjectReference[^>]*Biolog\.Api\.csproj' '$FEATURE/Biolog.Features.FaceSearch.csproj'"

# --- Module wiring ---
check "module lives one-way in api/Biolog.Api/Modules, not back in the feature project" \
  bash -c "test -f api/Biolog.Api/Modules/BioBio007Module.cs && ! test -f '$FEATURE/BioBio007Module.cs'"
check "registered in the packaged Program.cs module array" \
  bash -c "grep -q 'new BioBio007Module()' api/Biolog.Api/Program.cs"
check "no HTTP endpoints are mapped (AC01-AC05 are about the search/population/audit model, matching every prior BIO-BIO story's deferred-integration call)" \
  bash -c "! grep -q 'endpoints\.Map' api/Biolog.Api/Modules/BioBio007Module.cs"

# --- Developer test wiring ---
check "unit tests reference the feature project" \
  bash -c "grep -q 'Biolog.Features.FaceSearch.csproj' tests/features/bio-bio-007/Biolog.Features.FaceSearch.Tests.csproj"
check "docs/features/bio-bio-007/README.md documents the feature" test -f docs/features/bio-bio-007/README.md
check "builder evidence recorded" test -d evidence/features/bio-bio-007/builder

echo "=============================="
echo "$pass passed, $fail failed"
[ "$fail" -eq 0 ]
