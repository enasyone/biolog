#!/usr/bin/env bash
# Offline-safe structural checks for BIO-IDN-003 (institutional identifiers). Behavioural
# assertions (typed/tenant-scoped, configurable uniqueness, preserved history, no credential
# coupling, audit, HTTP pipeline) live in the executable xunit suites under
# tests/features/bio-idn-003/ and
# tests/dotnet/Biolog.IntegrationTests/InstitutionalIdentifierHostChannelTests.cs (see
# docs/features/bio-idn-003/README.md).
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"
pass=0; fail=0
check() { local label="$1"; shift; if "$@"; then echo "PASS  $label"; pass=$((pass+1)); else echo "FAIL  $label"; fail=$((fail+1)); fi; }

FEATURE=api/features/bio-idn-003

# --- AC01: identifiers are typed and tenant-scoped ---
check "AC01 feature project exists" test -f "$FEATURE/Biolog.Features.InstitutionalIdentifiers.csproj"
check "AC01 project targets the pinned .NET SDK's TFM" bash -c "grep -q '<TargetFramework>net10.0</TargetFramework>' '$FEATURE/Biolog.Features.InstitutionalIdentifiers.csproj'"
check "AC01 IdentifierType exists with the story's named types" \
  bash -c "grep -q 'MatriculationNumber' '$FEATURE/Domain/IdentifierType.cs' && grep -q 'StaffNumber' '$FEATURE/Domain/IdentifierType.cs' && grep -q 'TenantDefined' '$FEATURE/Domain/IdentifierType.cs'"
check "AC01 module lives one-way in api/Biolog.Api/Modules, not back in the feature project" \
  bash -c "test -f api/Biolog.Api/Modules/BioIdn003Module.cs && ! test -f '$FEATURE/BioIdn003Module.cs'"
check "AC01 registered in the packaged Program.cs module array" \
  bash -c "grep -q 'new BioIdn003Module()' api/Biolog.Api/Program.cs"
check "AC01 endpoints are versioned per ADR-0004 §1.1, nested under the owning person" \
  bash -c "grep -q '/api/v1/persons/{personId}/identifiers' '$FEATURE/Api/InstitutionalIdentifierEndpoints.cs'"
check "AC01 a test proves cross-tenant records are invisible through the wrong tenant" \
  bash -c "grep -q 'AC01_an_identifier_carries_its_type_and_is_only_visible_in_its_own_tenant' tests/features/bio-idn-003/InstitutionalIdentifierServiceTests.cs"
check "AC01 a test proves two different identifier types coexist for the same person" \
  bash -c "grep -q 'AC01_a_person_can_hold_a_matriculation_number_and_a_staff_number_at_once' tests/features/bio-idn-003/InstitutionalIdentifierServiceTests.cs"

# --- AC02: uniqueness rules are configurable per type ---
check "AC02 a separate configurable IdentifierUniquenessRule aggregate exists" \
  bash -c "test -f '$FEATURE/Domain/IdentifierUniquenessRule.cs' && grep -q 'Enforced' '$FEATURE/Domain/IdentifierUniquenessRule.cs'"
check "AC02 an admin endpoint to get/set the rule exists, separate from the person-scoped identifier routes" \
  bash -c "grep -q '/api/v1/identifier-types/{type}/uniqueness-rule' '$FEATURE/Api/InstitutionalIdentifierEndpoints.cs'"
check "AC02 a missing rule fails closed to Enforced = true" \
  bash -c "grep -q 'rule?.Enforced ?? true' '$FEATURE/InstitutionalIdentifierService.cs'"
check "AC02 a test proves the default enforces uniqueness across two different people" \
  bash -c "grep -q 'AC02_by_default_the_same_value_cannot_be_assigned_to_two_different_people_for_the_same_type' tests/features/bio-idn-003/InstitutionalIdentifierServiceTests.cs"
check "AC02 a test proves disabling enforcement for a type allows a shared value" \
  bash -c "grep -q 'AC02_disabling_enforcement_for_a_type_allows_the_same_value_for_two_people' tests/features/bio-idn-003/InstitutionalIdentifierServiceTests.cs"
check "AC02 a test proves uniqueness scope never crosses two different types" \
  bash -c "grep -q 'AC02_uniqueness_is_scoped_per_type_a_matric_number_and_a_staff_number_may_share_a_value' tests/features/bio-idn-003/InstitutionalIdentifierServiceTests.cs"
check "AC02 a test proves a retired value is never silently recycled while enforcement is on" \
  bash -c "grep -q 'AC02_a_retired_value_is_never_silently_recycled_when_enforcement_is_on' tests/features/bio-idn-003/InstitutionalIdentifierServiceTests.cs"

# --- BIO-IDN-003-QA-01 (Seyi round 1, High): the enforced-uniqueness scan is tenant-wide, not
# per-person - a per-person lock alone cannot serialize two different people racing to assign
# the same value ---
check "QA-01 AssignAsync acquires a lock keyed by (tenant, type, custom_label) - the same scope AC02's invariant spans" \
  bash -c "grep -q 'uniquenessLockRegistry.AcquireAsync(tenantId, type, customLabel' '$FEATURE/InstitutionalIdentifierService.cs'"
check "QA-01 that lock is acquired before the uniqueness/duplicate-value check and the commit" \
  bash -c "lock_line=\$(grep -n 'uniquenessLockRegistry.AcquireAsync' '$FEATURE/InstitutionalIdentifierService.cs' | head -1 | cut -d: -f1); check_line=\$(grep -n 'ValueExistsAsync(tenantId' '$FEATURE/InstitutionalIdentifierService.cs' | head -1 | cut -d: -f1); create_line=\$(grep -n 'repository.CreateAsync(identifier' '$FEATURE/InstitutionalIdentifierService.cs' | head -1 | cut -d: -f1); [ -n \"\$lock_line\" ] && [ -n \"\$check_line\" ] && [ -n \"\$create_line\" ] && [ \"\$lock_line\" -lt \"\$check_line\" ] && [ \"\$check_line\" -lt \"\$create_line\" ]"
check "QA-01 IdentifierUniquenessRuleService.SetRuleAsync shares the SAME lock registry type, so a rule change serializes against an in-flight assignment" \
  bash -c "grep -q 'IdentifierUniquenessRuleLockRegistry lockRegistry' '$FEATURE/IdentifierUniquenessRuleService.cs' && grep -q 'IdentifierUniquenessRuleLockRegistry uniquenessLockRegistry' '$FEATURE/InstitutionalIdentifierService.cs'"
check "QA-01 a break-it-on-purpose-verified test proves two different people racing the same value never both succeed" \
  bash -c "grep -q 'BIOIDN003_QA01_two_genuinely_concurrent_assigns_of_the_same_value_for_two_different_people_never_both_succeed' tests/features/bio-idn-003/InstitutionalIdentifierAuditTests.cs"
check "QA-01 closure item 2: a repository-level scan proves whether a (type, custom_label) scope already has a duplicated value" \
  bash -c "grep -q 'HasDuplicateValuesAsync' '$FEATURE/Domain/IInstitutionalIdentifierRepository.cs' && grep -q 'HasDuplicateValuesAsync' '$FEATURE/Domain/InMemoryInstitutionalIdentifierRepository.cs'"
check "QA-01 closure item 2: SetRuleAsync rejects enabling enforcement over an already-duplicated scope" \
  bash -c "grep -q 'UniquenessAlreadyViolated' '$FEATURE/Domain/InstitutionalIdentifierError.cs' && grep -q 'HasDuplicateValuesAsync' '$FEATURE/IdentifierUniquenessRuleService.cs'"
check "QA-01 closure item 2: tests prove both the rejection and that a clean scope still succeeds" \
  bash -c "grep -q 'QA01_enabling_enforcement_over_an_already_duplicated_scope_is_rejected' tests/features/bio-idn-003/InstitutionalIdentifierServiceTests.cs && grep -q 'QA01_enabling_enforcement_over_a_clean_scope_still_succeeds' tests/features/bio-idn-003/InstitutionalIdentifierServiceTests.cs"

# --- AC03: history/status is preserved ---
check "AC03 IdentifierStatus is two states, Active/Retired, never a delete" \
  bash -c "grep -q 'Active,' '$FEATURE/Domain/IdentifierStatus.cs' && grep -q 'Retired,' '$FEATURE/Domain/IdentifierStatus.cs' && ! grep -qi 'DeleteAsync\|RemoveAsync' '$FEATURE/Domain/IInstitutionalIdentifierRepository.cs'"
check "AC03 Value is immutable once created - get-only, never mutated in place" \
  bash -c "grep -q 'public string Value { get; }' '$FEATURE/Domain/InstitutionalIdentifier.cs'"
check "AC03 SequenceNumber gives every reissued identifier its own historical record" \
  bash -c "grep -q 'public int SequenceNumber { get; }' '$FEATURE/Domain/InstitutionalIdentifier.cs' && grep -q 'ListHistoryAsync' '$FEATURE/Domain/IInstitutionalIdentifierRepository.cs'"
check "AC03 a test proves retiring preserves the record rather than deleting it" \
  bash -c "grep -q 'AC03_retiring_an_identifier_preserves_the_record_and_marks_it_retired' tests/features/bio-idn-003/InstitutionalIdentifierServiceTests.cs"
check "AC03 a test proves a reissued identifier is a NEW record and the old one survives as history" \
  bash -c "grep -q 'AC03_a_reissued_identifier_is_a_new_record_at_the_next_sequence_number_and_the_old_one_survives_as_history' tests/features/bio-idn-003/InstitutionalIdentifierServiceTests.cs"

# --- AC04: electronic credentials use opaque credential ids (this feature never becomes one) ---
check "AC04 no field anywhere in this feature is named credential_id or similar (code, not prose)" \
  bash -c "! grep -rv '^\s*///\|^\s*//' '$FEATURE' --include='*.cs' | grep -qi 'credentialid\|credential_id'"
check "AC04 no authentication/verify/login endpoint exists on this feature's routes" \
  bash -c "! grep -qiE 'verify|authenticate|login' '$FEATURE/Api/InstitutionalIdentifierEndpoints.cs'"
check "AC04 a structural test proves no credential-named member exists by reflection" \
  bash -c "grep -q 'No_field_or_method_anywhere_on_this_service_or_the_aggregate_names_a_credential_id' tests/features/bio-idn-003/InstitutionalIdentifierServiceTests.cs"
check "AC04 a structural test proves no authentication method exists by reflection" \
  bash -c "grep -q 'No_authentication_or_verification_method_exists_anywhere_on_this_service' tests/features/bio-idn-003/InstitutionalIdentifierServiceTests.cs"
check "AC04 no ProjectReference to BIO-IDN-001's project (existence is proven through a narrow gateway, not a runtime coupling)" \
  bash -c "! grep -q '<ProjectReference.*bio-idn-001' '$FEATURE/Biolog.Features.InstitutionalIdentifiers.csproj'"

# --- Applying BIO-IDN-002-QA-01 from the start (not waiting for a review round to find it here too) ---
check "a narrow, read-only IPersonExistenceGateway interface exists, owned by this feature" \
  bash -c "test -f '$FEATURE/Domain/IPersonExistenceGateway.cs' && grep -q 'internal interface IPersonExistenceGateway' '$FEATURE/Domain/IPersonExistenceGateway.cs'"
check "AssignAsync calls the gateway before the lock and before any audit write" \
  bash -c "grep -q 'personGateway.ExistsAsync' '$FEATURE/InstitutionalIdentifierService.cs'"
check "a missing or foreign-tenant person is rejected as person_not_found (404)" \
  bash -c "grep -q 'PersonNotFound' '$FEATURE/Domain/InstitutionalIdentifierError.cs' && grep -A2 'PersonNotFound(string personId)' '$FEATURE/Domain/InstitutionalIdentifierError.cs' | grep -q ', 404,'"
check "the real adapter wraps BIO-IDN-001's own tenant-scoped PersonService.GetAsync (no reimplemented tenant logic)" \
  bash -c "grep -q 'PersonExistenceGatewayAdapter' api/Biolog.Api/Modules/BioIdn003Module.cs && grep -q 'personService.GetAsync' api/Biolog.Api/Modules/BioIdn003Module.cs"
check "unit tests prove both a wholly-unknown person and a fail-closed non-audited attempt" \
  bash -c "grep -q 'Assigning_an_identifier_for_a_person_that_does_not_exist_in_the_resolved_tenant_is_rejected' tests/features/bio-idn-003/InstitutionalIdentifierServiceTests.cs && grep -q 'The_existence_check_fails_closed_no_audit_event_and_nothing_is_stored' tests/features/bio-idn-003/InstitutionalIdentifierServiceTests.cs"
check "real-packaged-host tests prove an unknown person and a real tenant-A person via tenant-B's host both fail identically" \
  bash -c "grep -q 'BIOIDN003_assigning_an_identifier_for_an_unknown_canonical_person_id_is_rejected' tests/dotnet/Biolog.IntegrationTests/InstitutionalIdentifierHostChannelTests.cs && grep -q 'BIOIDN003_a_tenant_B_caller_cannot_attach_an_identifier_to_a_real_tenant_A_person' tests/dotnet/Biolog.IntegrationTests/InstitutionalIdentifierHostChannelTests.cs"

# --- Governance, applying BIO-IDN-001-QA-01's lesson from the start ---
check "governance: IInstitutionalIdentifierRepository and InMemoryInstitutionalIdentifierRepository are internal, not public" \
  bash -c "grep -q '^internal interface IInstitutionalIdentifierRepository' '$FEATURE/Domain/IInstitutionalIdentifierRepository.cs' && grep -q '^internal sealed class InMemoryInstitutionalIdentifierRepository' '$FEATURE/Domain/InMemoryInstitutionalIdentifierRepository.cs'"
check "governance: InstitutionalIdentifier's constructor is internal, not a public object-initializer surface" \
  bash -c "grep -q '    internal InstitutionalIdentifier(string personId, string tenantId, IdentifierType type, string? customLabel, int sequenceNumber, string value)' '$FEATURE/Domain/InstitutionalIdentifier.cs'"
check "governance: identity fields are get-only - immutable even via a 'with' expression" \
  bash -c "grep -q 'public string PersonId { get; }' '$FEATURE/Domain/InstitutionalIdentifier.cs' && grep -q 'public string TenantId { get; }' '$FEATURE/Domain/InstitutionalIdentifier.cs' && grep -q 'public string Value { get; }' '$FEATURE/Domain/InstitutionalIdentifier.cs'"
check "governance: the InternalsVisibleTo seam is explicit and auditable in source control" \
  bash -c "grep -q '<InternalsVisibleTo Include=\"Biolog.Api\" />' '$FEATURE/Biolog.Features.InstitutionalIdentifiers.csproj' && grep -q '<InternalsVisibleTo Include=\"Biolog.Features.InstitutionalIdentifiers.Tests\" />' '$FEATURE/Biolog.Features.InstitutionalIdentifiers.csproj'"
check "governance: the repository independently re-validates identity immutability and initial state at the persistence boundary" \
  bash -c "grep -q 'IdentityImmutable' '$FEATURE/Domain/InMemoryInstitutionalIdentifierRepository.cs' && grep -q 'InvalidInitialState' '$FEATURE/Domain/InMemoryInstitutionalIdentifierRepository.cs'"
check "governance: a reflection test proves the surface is inaccessible to ordinary production consumers" \
  bash -c "grep -q 'IInstitutionalIdentifierRepository_and_InMemoryInstitutionalIdentifierRepository_are_not_public' tests/features/bio-idn-003/InstitutionalIdentifierRepositoryGovernanceTests.cs && grep -q 'InstitutionalIdentifier_has_no_publicly_accessible_constructor' tests/features/bio-idn-003/InstitutionalIdentifierRepositoryGovernanceTests.cs"

# --- Tenant boundary is mandatory, inherited from every sibling story's own pattern ---
check "the service reads the resolved tenant, never a caller-supplied id" \
  bash -c "[ \"\$(grep -c 'RequireTenant()' '$FEATURE/InstitutionalIdentifierService.cs')\" -ge 3 ]"
check "a missing tenant context fails closed (tenant_context_missing, 400)" \
  bash -c "grep -q 'TenantContextMissing' '$FEATURE/Domain/InstitutionalIdentifierError.cs' && grep -A2 'TenantContextMissing()' '$FEATURE/Domain/InstitutionalIdentifierError.cs' | grep -q ', 400)'"
check "the repository asserts a record's own tenant_id on every read/write" \
  bash -c "grep -q 'current.TenantId != expected.TenantId\|identifier.TenantId != tenantId' '$FEATURE/Domain/InMemoryInstitutionalIdentifierRepository.cs'"
check "real shipped-host positive-path test exists (ADR-0002 Decision 6 obligation)" \
  bash -c "test -f tests/dotnet/Biolog.IntegrationTests/InstitutionalIdentifierHostChannelTests.cs && grep -q 'WebApplicationFactory<Program>' tests/dotnet/Biolog.IntegrationTests/InstitutionalIdentifierHostChannelTests.cs"

# --- ADR-0003 Decision 7: audit-atomicity ---
check "Decision 7 element 1: per-aggregate locks exist, each in its own singleton-safe class" \
  bash -c "test -f '$FEATURE/InstitutionalIdentifierLockRegistry.cs' && grep -q 'AcquireAsync' '$FEATURE/InstitutionalIdentifierLockRegistry.cs' && test -f '$FEATURE/IdentifierUniquenessRuleLockRegistry.cs'"
check "the locks live in their own classes because both services must be Scoped (captive-dependency avoidance)" \
  bash -c "grep -q 'AddScoped<InstitutionalIdentifierService>' api/Biolog.Api/Modules/BioIdn003Module.cs && grep -q 'AddSingleton<InstitutionalIdentifierLockRegistry>' api/Biolog.Api/Modules/BioIdn003Module.cs && grep -q 'AddSingleton<IdentifierUniquenessRuleLockRegistry>' api/Biolog.Api/Modules/BioIdn003Module.cs"
check "Decision 7 element 2: the repository commit is the return statement (audit already happened above it)" \
  bash -c "grep -qE 'return await repository\.(CreateAsync|ReplaceAsync)\(' '$FEATURE/InstitutionalIdentifierService.cs'"
check "Decision 7 element 3: ExistsAsync verification on an ambiguous audit-write throw" \
  bash -c "grep -q 'ExistsAsync' '$FEATURE/Audit/IInstitutionalIdentifierAuditSink.cs' && grep -q 'auditSink.ExistsAsync' '$FEATURE/InstitutionalIdentifierService.cs'"
check "Decision 7 element 4: CancellationToken.None on every commit once the audit is confirmed" \
  bash -c "[ \"\$(grep -c 'repository\.\(CreateAsync\|ReplaceAsync\)([^)]*, CancellationToken\.None)' '$FEATURE/InstitutionalIdentifierService.cs')\" = 2 ]"
check "a same-identifier concurrency race is proven" \
  bash -c "grep -q 'Two_genuinely_concurrent_retires_of_the_same_identifier_never_produce_a_false_allowed_audit' tests/features/bio-idn-003/InstitutionalIdentifierAuditTests.cs"
check "audit sink failure never leaves a committed-but-unaudited record (assign/retire)" \
  bash -c "grep -q 'Assign_is_never_committed_when_the_audit_sink_fails' tests/features/bio-idn-003/InstitutionalIdentifierAuditTests.cs && grep -q 'Retire_is_never_committed_when_the_audit_sink_fails' tests/features/bio-idn-003/InstitutionalIdentifierAuditTests.cs"
check "a cancellation arriving right after a confirmed audit write does not abort the commit" \
  bash -c "grep -q 'A_cancellation_arriving_right_after_the_audit_write_succeeds_does_not_abort_the_commit' tests/features/bio-idn-003/InstitutionalIdentifierAuditTests.cs"

# --- Audit trail privacy (contracts/schemas/data-classification.json names "institutional identifier" as personal) ---
check "the audit event builder registers 'personal' classification, matching BIO-IDN-001's identical posture" \
  bash -c "grep -q '\[\"personal\"\] = 2' '$FEATURE/Audit/InstitutionalIdentifierAuditEventBuilder.cs'"
check "the service never places the raw identifier value into an audit event's Detail - it is withheld" \
  bash -c "grep -q 'withheld: ValueFieldNames' '$FEATURE/InstitutionalIdentifierService.cs' && grep -q 'ValueFieldNames = \[\"value\"\]' '$FEATURE/InstitutionalIdentifierService.cs'"
check "a test proves the withheld/no-leak posture directly" \
  bash -c "grep -q 'Assign_records_exactly_one_allowed_audit_event_carrying_the_real_tenant_id_and_withholding_the_value' tests/features/bio-idn-003/InstitutionalIdentifierAuditTests.cs"

# --- Developer test wiring ---
check "unit/audit/scope tests reference the feature project" \
  bash -c "grep -q 'Biolog.Features.InstitutionalIdentifiers.csproj' tests/features/bio-idn-003/Biolog.Features.InstitutionalIdentifiers.Tests.csproj"
check "feature-local integration tests exist and reference TestHost" \
  bash -c "test -f tests/features/bio-idn-003/InstitutionalIdentifierEndpointsIntegrationTests.cs && grep -q 'Microsoft.AspNetCore.TestHost' tests/features/bio-idn-003/Biolog.Features.InstitutionalIdentifiers.Tests.csproj"
check "docs/features/bio-idn-003/README.md documents the feature" test -f docs/features/bio-idn-003/README.md
check "builder evidence recorded" test -d evidence/features/bio-idn-003/builder

echo "=============================="
echo "$pass passed, $fail failed"
[ "$fail" -eq 0 ]
