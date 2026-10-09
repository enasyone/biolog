#!/usr/bin/env bash
# Offline-safe structural checks for BIO-IDN-002 (configurable person profiles and hierarchy
# assignment). Behavioural assertions (multiple profiles, hierarchy targeting, independent
# activation/expiry, no duplicate person, audit, HTTP pipeline) live in the executable xunit
# suites under tests/features/bio-idn-002/ and
# tests/dotnet/Biolog.IntegrationTests/PersonProfileHostChannelTests.cs (see
# docs/features/bio-idn-002/README.md).
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"
pass=0; fail=0
check() { local label="$1"; shift; if "$@"; then echo "PASS  $label"; pass=$((pass+1)); else echo "FAIL  $label"; fail=$((fail+1)); fi; }

FEATURE=api/features/bio-idn-002

# --- AC01: multiple simultaneous profiles are supported ---
check "AC01 feature project exists" test -f "$FEATURE/Biolog.Features.PersonProfileRegistry.csproj"
check "AC01 project targets the pinned .NET SDK's TFM" bash -c "grep -q '<TargetFramework>net10.0</TargetFramework>' '$FEATURE/Biolog.Features.PersonProfileRegistry.csproj'"
check "AC01 the repository's only uniqueness rule is (PersonId, Type), not one-profile-per-person" \
  bash -c "grep -q '_profiles.TryAdd' '$FEATURE/Domain/InMemoryPersonProfileRepository.cs'"
check "AC01 a test proves two different profile types coexist for the same person" \
  bash -c "grep -q 'AC01_a_person_can_hold_two_different_profile_types_at_once' tests/features/bio-idn-002/PersonProfileServiceTests.cs"
check "AC01 module lives one-way in api/Biolog.Api/Modules, not back in the feature project" \
  bash -c "test -f api/Biolog.Api/Modules/BioIdn002Module.cs && ! test -f '$FEATURE/BioIdn002Module.cs'"
check "AC01 registered in the packaged Program.cs module array" \
  bash -c "grep -q 'new BioIdn002Module()' api/Biolog.Api/Program.cs"
check "AC01 endpoints are versioned per ADR-0004 §1.1, nested under the owning person" \
  bash -c "grep -q '/api/v1/persons/{personId}/profiles' '$FEATURE/Api/PersonProfileEndpoints.cs'"

# --- AC02: profiles can target organisation/campus/branch ---
check "AC02 OrganisationId/CampusId/BranchId exist on the aggregate and the request DTO" \
  bash -c "grep -q 'OrganisationId' '$FEATURE/Domain/PersonProfile.cs' && grep -q 'CampusId' '$FEATURE/Domain/PersonProfile.cs' && grep -q 'BranchId' '$FEATURE/Domain/PersonProfile.cs' && grep -q 'organisation_id' '$FEATURE/Api/PersonProfileRequests.cs'"
check "AC02 a test proves all three hierarchy fields round-trip" \
  bash -c "grep -q 'AC02_a_profile_can_target_organisation_campus_and_branch' tests/features/bio-idn-002/PersonProfileServiceTests.cs"
check "AC02 a test proves hierarchy targeting is optional" \
  bash -c "grep -q 'AC02_hierarchy_fields_are_optional' tests/features/bio-idn-002/PersonProfileServiceTests.cs"

# --- AC03: profile activation/expiry is independent ---
check "AC03 ActivatedAt and ExpiresAt exist, per-profile, not derived from PERSON's own status" \
  bash -c "grep -q 'ActivatedAt' '$FEATURE/Domain/PersonProfile.cs' && grep -q 'ExpiresAt' '$FEATURE/Domain/PersonProfile.cs'"
check "AC03 in-effect-ness is computed at read time, not a background-mutated stored field" \
  bash -c "grep -q 'IsCurrentlyInEffect' '$FEATURE/Domain/PersonProfile.cs'"
check "AC03 expires_at is validated as a real UTC instant (ADR-0001 §2.1), not a bare date" \
  bash -c "grep -q 'UtcInstantPattern' '$FEATURE/PersonProfileService.cs'"
check "AC03 a test proves two profiles for the same person have independent activation/expiry" \
  bash -c "grep -q 'AC03_two_profiles_for_the_same_person_have_fully_independent_activation_and_expiry' tests/features/bio-idn-002/PersonProfileServiceTests.cs"
check "AC03 a test proves a past expiry is not in effect even while Status is still ACTIVE" \
  bash -c "grep -q 'AC03_a_profile_with_a_past_expiry_is_not_currently_in_effect_even_while_status_is_still_ACTIVE' tests/features/bio-idn-002/PersonProfileServiceTests.cs"

# --- AC04: no duplicate PERSON is created solely because of a second role ---
check "AC04 no person-creation method exists anywhere on this service (property/method declarations, not prose)" \
  bash -c "! grep -v '^\s*///\|^\s*//' '$FEATURE/PersonProfileService.cs' | grep -qiE 'RegisterPersonAsync|CreatePersonAsync'"
check "AC04 a structural test proves the omission adversarially" \
  bash -c "grep -q 'No_person_creation_method_exists_anywhere_on_this_service' tests/features/bio-idn-002/PersonProfileServiceTests.cs"
check "AC04 assigning a second profile type never creates a second person - proven directly" \
  bash -c "grep -q 'AC04_assigning_a_second_profile_type_never_creates_a_second_person' tests/features/bio-idn-002/PersonProfileServiceTests.cs"
check "AC04 duplicate profile type for the same person is rejected (409)" \
  bash -c "grep -q 'DuplicateProfileType' '$FEATURE/Domain/PersonProfileError.cs' && grep -A2 'DuplicateProfileType(string personId, ProfileType type)' '$FEATURE/Domain/PersonProfileError.cs' | grep -q ', 409,'"
check "AC04 no ProjectReference to BIO-IDN-001's project (existence is proven through a narrow gateway, not a runtime coupling)" \
  bash -c "! grep -q '<ProjectReference.*bio-idn-001' '$FEATURE/Biolog.Features.PersonProfileRegistry.csproj'"

# --- BIO-IDN-002-QA-01 (Seyi round 1): AssignAsync must prove the PERSON exists in the
# resolved tenant, not merely validate person_id's syntax ---
check "QA-01 a narrow, read-only IPersonExistenceGateway interface exists, owned by this feature" \
  bash -c "test -f '$FEATURE/Domain/IPersonExistenceGateway.cs' && grep -q 'internal interface IPersonExistenceGateway' '$FEATURE/Domain/IPersonExistenceGateway.cs'"
check "QA-01 AssignAsync calls the gateway before the lock and before any audit write" \
  bash -c "grep -q 'personGateway.ExistsAsync' '$FEATURE/PersonProfileService.cs'"
check "QA-01 a missing or foreign-tenant person is rejected as person_not_found (404)" \
  bash -c "grep -q 'PersonNotFound' '$FEATURE/Domain/PersonProfileError.cs' && grep -A2 'PersonNotFound(string personId)' '$FEATURE/Domain/PersonProfileError.cs' | grep -q ', 404,'"
check "QA-01 the real adapter wraps BIO-IDN-001's own tenant-scoped PersonService.GetAsync (no reimplemented tenant logic)" \
  bash -c "grep -q 'PersonExistenceGatewayAdapter' api/Biolog.Api/Modules/BioIdn002Module.cs && grep -q 'personService.GetAsync' api/Biolog.Api/Modules/BioIdn002Module.cs"
check "QA-01 unit tests prove both a wholly-unknown person and a fail-closed non-audited attempt" \
  bash -c "grep -q 'QA01_assigning_a_profile_for_a_person_that_does_not_exist_in_the_resolved_tenant_is_rejected' tests/features/bio-idn-002/PersonProfileServiceTests.cs && grep -q 'QA01_the_existence_check_fails_closed_no_audit_event_and_nothing_is_stored' tests/features/bio-idn-002/PersonProfileServiceTests.cs"
check "QA-01 real-packaged-host tests prove an unknown person and a real tenant-A person via tenant-B's host both fail identically" \
  bash -c "grep -q 'BIOIDN002_QA01_assigning_a_profile_for_an_unknown_canonical_person_id_is_rejected' tests/dotnet/Biolog.IntegrationTests/PersonProfileHostChannelTests.cs && grep -q 'BIOIDN002_QA01_a_tenant_B_caller_cannot_attach_a_profile_to_a_real_tenant_A_person' tests/dotnet/Biolog.IntegrationTests/PersonProfileHostChannelTests.cs"

# --- Governance, applying BIO-IDN-001-QA-01's lesson from the start (not after a review round) ---
check "governance: IPersonProfileRepository and InMemoryPersonProfileRepository are internal, not public" \
  bash -c "grep -q '^internal interface IPersonProfileRepository' '$FEATURE/Domain/IPersonProfileRepository.cs' && grep -q '^internal sealed class InMemoryPersonProfileRepository' '$FEATURE/Domain/InMemoryPersonProfileRepository.cs'"
check "governance: PersonProfile's constructor is internal, not a public object-initializer surface" \
  bash -c "grep -q '    internal PersonProfile(string personId, string tenantId, ProfileType type, string? customLabel)' '$FEATURE/Domain/PersonProfile.cs'"
check "governance: PersonId/TenantId/Type are get-only - immutable even via a 'with' expression" \
  bash -c "grep -q 'public string PersonId { get; }' '$FEATURE/Domain/PersonProfile.cs' && grep -q 'public string TenantId { get; }' '$FEATURE/Domain/PersonProfile.cs' && grep -q 'public ProfileType Type { get; }' '$FEATURE/Domain/PersonProfile.cs'"
check "governance: the InternalsVisibleTo seam is explicit and auditable in source control" \
  bash -c "grep -q '<InternalsVisibleTo Include=\"Biolog.Api\" />' '$FEATURE/Biolog.Features.PersonProfileRegistry.csproj' && grep -q '<InternalsVisibleTo Include=\"Biolog.Features.PersonProfileRegistry.Tests\" />' '$FEATURE/Biolog.Features.PersonProfileRegistry.csproj'"
check "governance: the repository independently re-validates identity immutability and initial state at the persistence boundary" \
  bash -c "grep -q 'IdentityImmutable' '$FEATURE/Domain/InMemoryPersonProfileRepository.cs' && grep -q 'InvalidInitialState' '$FEATURE/Domain/InMemoryPersonProfileRepository.cs'"
check "governance: a reflection test proves the surface is inaccessible to ordinary production consumers" \
  bash -c "grep -q 'IPersonProfileRepository_and_InMemoryPersonProfileRepository_are_not_public' tests/features/bio-idn-002/PersonProfileRepositoryGovernanceTests.cs && grep -q 'PersonProfile_has_no_publicly_accessible_constructor' tests/features/bio-idn-002/PersonProfileRepositoryGovernanceTests.cs"

# --- AC02 (tenant boundary is mandatory, inherited from BIO-IDN-001's own AC02 pattern) ---
check "the service reads the resolved tenant, never a caller-supplied id" \
  bash -c "[ \"\$(grep -c 'RequireTenant()' '$FEATURE/PersonProfileService.cs')\" -ge 3 ]"
check "a missing tenant context fails closed (tenant_context_missing, 400)" \
  bash -c "grep -q 'TenantContextMissing' '$FEATURE/Domain/PersonProfileError.cs' && grep -A2 'TenantContextMissing()' '$FEATURE/Domain/PersonProfileError.cs' | grep -q ', 400)'"
check "the repository asserts a record's own tenant_id on every read/write" \
  bash -c "grep -q 'current.TenantId != expected.TenantId\|profile.TenantId != tenantId' '$FEATURE/Domain/InMemoryPersonProfileRepository.cs'"
check "real shipped-host positive-path test exists (ADR-0002 Decision 6 obligation)" \
  bash -c "test -f tests/dotnet/Biolog.IntegrationTests/PersonProfileHostChannelTests.cs && grep -q 'WebApplicationFactory<Program>' tests/dotnet/Biolog.IntegrationTests/PersonProfileHostChannelTests.cs"

# --- ADR-0003 Decision 7: audit-atomicity ---
check "Decision 7 element 1: a per-aggregate lock exists, in its own singleton-safe class" \
  bash -c "test -f '$FEATURE/PersonProfileLockRegistry.cs' && grep -q 'AcquireAsync' '$FEATURE/PersonProfileLockRegistry.cs'"
check "the lock lives in its own class because PersonProfileService must be Scoped (captive-dependency avoidance)" \
  bash -c "grep -q 'AddScoped<PersonProfileService>' api/Biolog.Api/Modules/BioIdn002Module.cs && grep -q 'AddSingleton<PersonProfileLockRegistry>' api/Biolog.Api/Modules/BioIdn002Module.cs"
check "Decision 7 element 2: the repository commit is the return statement (audit already happened above it)" \
  bash -c "grep -qE 'return await repository\.(CreateAsync|ReplaceAsync)\(' '$FEATURE/PersonProfileService.cs'"
check "Decision 7 element 3: ExistsAsync verification on an ambiguous audit-write throw" \
  bash -c "grep -q 'ExistsAsync' '$FEATURE/Audit/IPersonProfileAuditSink.cs' && grep -q 'auditSink.ExistsAsync' '$FEATURE/PersonProfileService.cs'"
check "Decision 7 element 4: CancellationToken.None on every commit once the audit is confirmed" \
  bash -c "[ \"\$(grep -c 'repository\.\(CreateAsync\|ReplaceAsync\)([^)]*, CancellationToken\.None)' '$FEATURE/PersonProfileService.cs')\" = 2 ]"
check "a same-profile concurrency race is proven" \
  bash -c "grep -q 'Two_genuinely_concurrent_revokes_of_the_same_profile_never_produce_a_false_allowed_audit' tests/features/bio-idn-002/PersonProfileAuditTests.cs"
check "audit sink failure never leaves a committed-but-unaudited record (assign/revoke)" \
  bash -c "grep -q 'Assign_is_never_committed_when_the_audit_sink_fails' tests/features/bio-idn-002/PersonProfileAuditTests.cs && grep -q 'Revoke_is_never_committed_when_the_audit_sink_fails' tests/features/bio-idn-002/PersonProfileAuditTests.cs"
check "a cancellation arriving right after a confirmed audit write does not abort the commit" \
  bash -c "grep -q 'A_cancellation_arriving_right_after_the_audit_write_succeeds_does_not_abort_the_commit' tests/features/bio-idn-002/PersonProfileAuditTests.cs"

# --- Developer test wiring ---
check "unit/audit/scope tests reference the feature project" \
  bash -c "grep -q 'Biolog.Features.PersonProfileRegistry.csproj' tests/features/bio-idn-002/Biolog.Features.PersonProfileRegistry.Tests.csproj"
check "feature-local integration tests exist and reference TestHost" \
  bash -c "test -f tests/features/bio-idn-002/PersonProfileEndpointsIntegrationTests.cs && grep -q 'Microsoft.AspNetCore.TestHost' tests/features/bio-idn-002/Biolog.Features.PersonProfileRegistry.Tests.csproj"
check "docs/features/bio-idn-002/README.md documents the feature" test -f docs/features/bio-idn-002/README.md
check "builder evidence recorded" test -d evidence/features/bio-idn-002/builder

echo "=============================="
echo "$pass passed, $fail failed"
[ "$fail" -eq 0 ]
