#!/usr/bin/env bash
# Offline-safe structural checks for BIO-IDN-001 (universal PERSON aggregate).
# Behavioural assertions (lifecycle, tenant boundary, audit, HTTP pipeline) live in the
# executable xunit suites under tests/features/bio-idn-001/ and
# tests/dotnet/Biolog.IntegrationTests/PersonHostChannelTests.cs (see
# docs/features/bio-idn-001/README.md).
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"
pass=0; fail=0
check() { local label="$1"; shift; if "$@"; then echo "PASS  $label"; pass=$((pass+1)); else echo "FAIL  $label"; fail=$((fail+1)); fi; }

FEATURE=api/features/bio-idn-001

# --- AC01: PERSON exists independently of student/staff/visitor roles ---
check "AC01 feature project exists" test -f "$FEATURE/Biolog.Features.PersonRegistry.csproj"
check "AC01 project targets the pinned .NET SDK's TFM" bash -c "grep -q '<TargetFramework>net10.0</TargetFramework>' '$FEATURE/Biolog.Features.PersonRegistry.csproj'"
check "AC01 Person carries no role/profile field anywhere (property declarations, not prose)" \
  bash -c "! grep -v '^\s*///' '$FEATURE/Domain/Person.cs' '$FEATURE/Api/PersonRequests.cs' '$FEATURE/Api/PersonResponse.cs' | grep -qiE 'string\?? *Role|string\?? *Profile|ProfileType'"
check "AC01 a structural test proves the omission adversarially, not just by inspection" \
  bash -c "grep -q 'No_role_or_profile_field_exists_on_the_registration_request_or_the_aggregate' tests/features/bio-idn-001/PersonServiceTests.cs"
check "AC01 module lives one-way in api/Biolog.Api/Modules, not back in the feature project" \
  bash -c "test -f api/Biolog.Api/Modules/BioIdn001Module.cs && ! test -f '$FEATURE/BioIdn001Module.cs'"
check "AC01 registered in the packaged Program.cs module array" \
  bash -c "grep -q 'new BioIdn001Module()' api/Biolog.Api/Program.cs"
check "AC01 endpoints are versioned per ADR-0004 §1.1, tenant-plane (no /control-plane prefix)" \
  bash -c "grep -q '\"/api/v1/persons\"' '$FEATURE/Api/PersonEndpoints.cs'"

# --- AC02: tenant boundary is mandatory ---
check "AC02 this feature defines its own tenant-context seam, not a reference to Biolog.Api.Tenancy" \
  bash -c "test -f '$FEATURE/Tenancy/ICurrentTenantProvider.cs' && ! grep -q '<ProjectReference.*Biolog\.Api\.csproj' '$FEATURE/Biolog.Features.PersonRegistry.csproj'"
check "AC02 the register request carries no tenant_id field (a caller cannot pick a tenant)" \
  bash -c "! grep -v '^\s*///' '$FEATURE/Api/PersonRequests.cs' | grep -qi 'tenant_id'"
check "AC02 the service reads the resolved tenant, never a caller-supplied id (RequireTenant call sites)" \
  bash -c "[ \"\$(grep -c 'RequireTenant()' '$FEATURE/PersonService.cs')\" -ge 4 ]"
check "AC02 a missing tenant context fails closed (tenant_context_missing, 400)" \
  bash -c "grep -q 'TenantContextMissing' '$FEATURE/Domain/PersonError.cs' && grep -A2 'TenantContextMissing()' '$FEATURE/Domain/PersonError.cs' | grep -q ', 400)'"
check "AC02 the repository asserts a record's own tenant_id on every read/write" \
  bash -c "grep -q 'person.TenantId != tenantId' '$FEATURE/Domain/InMemoryPersonRepository.cs'"
check "AC02 cross-tenant lookup is 404, not 403 (no enumeration oracle, ADR-0001 §1.5)" \
  bash -c "grep -q 'Cross-tenant lookup returns null' '$FEATURE/Domain/InMemoryPersonRepository.cs'"
check "AC02 real shipped-host positive-path test exists (ADR-0002 Decision 6 obligation)" \
  bash -c "test -f tests/dotnet/Biolog.IntegrationTests/PersonHostChannelTests.cs && grep -q 'WebApplicationFactory<Program>' tests/dotnet/Biolog.IntegrationTests/PersonHostChannelTests.cs"
check "AC02 the real host-channel test proves tenant resolution through an actually-verified BIO-TEN-002 domain" \
  bash -c "grep -q '/api/v1/control-plane/tenant-domains' tests/dotnet/Biolog.IntegrationTests/PersonHostChannelTests.cs"

# --- BIO-IDN-001-QA-01 (Seyi round 1): the persistence boundary itself must refuse a tenant
# move, an altered identifier, an illegal lifecycle jump, and be inaccessible to ordinary
# production consumers ---
check "QA-01 IPersonRepository and InMemoryPersonRepository are internal, not public" \
  bash -c "grep -q '^internal interface IPersonRepository' '$FEATURE/Domain/IPersonRepository.cs' && grep -q '^internal sealed class InMemoryPersonRepository' '$FEATURE/Domain/InMemoryPersonRepository.cs'"
check "QA-01 Person's constructor is internal, not a public object-initializer surface" \
  bash -c "grep -q '    internal Person(string id, string tenantId)' '$FEATURE/Domain/Person.cs'"
check "QA-01 Id and TenantId are get-only (no init/set) - immutable even via a 'with' expression" \
  bash -c "grep -q 'public string Id { get; }' '$FEATURE/Domain/Person.cs' && grep -q 'public string TenantId { get; }' '$FEATURE/Domain/Person.cs'"
check "QA-01 the InternalsVisibleTo seam is explicit and auditable in source control" \
  bash -c "grep -q '<InternalsVisibleTo Include=\"Biolog.Api\" />' '$FEATURE/Biolog.Features.PersonRegistry.csproj' && grep -q '<InternalsVisibleTo Include=\"Biolog.Features.PersonRegistry.Tests\" />' '$FEATURE/Biolog.Features.PersonRegistry.csproj'"
check "QA-01 the repository independently re-validates identity/tenant immutability at the persistence boundary" \
  bash -c "grep -q 'IdentityOrTenantImmutable' '$FEATURE/Domain/InMemoryPersonRepository.cs'"
check "QA-01 the repository independently re-validates lifecycle legality via the SAME shared transition table PersonService uses" \
  bash -c "test -f '$FEATURE/Domain/PersonLifecycleTransitions.cs' && grep -q 'PersonLifecycleTransitions.IsAllowed' '$FEATURE/Domain/InMemoryPersonRepository.cs' && grep -q 'PersonLifecycleTransitions.IsAllowed' '$FEATURE/PersonService.cs'"
check "QA-01 CreateAsync rejects a person whose initial state is not PRE_ENROLLED at version 1" \
  bash -c "grep -q 'InvalidInitialState' '$FEATURE/Domain/InMemoryPersonRepository.cs'"
check "QA-01 tests reproduce every attack the review named: foreign tenant, unrelated identity, illegal jump, no audit dependency" \
  bash -c "test -f tests/features/bio-idn-001/PersonRepositoryGovernanceTests.cs && grep -q 'ReplaceAsync_rejects_moving_a_person_to_a_foreign_tenant' tests/features/bio-idn-001/PersonRepositoryGovernanceTests.cs && grep -q 'ReplaceAsync_rejects_two_unrelated_person_instances_with_different_ids' tests/features/bio-idn-001/PersonRepositoryGovernanceTests.cs && grep -q 'ReplaceAsync_rejects_an_illegal_lifecycle_jump' tests/features/bio-idn-001/PersonRepositoryGovernanceTests.cs && grep -q 'InMemoryPersonRepository_has_no_audit_sink_dependency_of_any_kind' tests/features/bio-idn-001/PersonRepositoryGovernanceTests.cs"
check "QA-01 a reflection test proves the surface is inaccessible to ordinary production consumers, not merely asserted" \
  bash -c "grep -q 'IPersonRepository_and_InMemoryPersonRepository_are_not_public' tests/features/bio-idn-001/PersonRepositoryGovernanceTests.cs && grep -q 'Person_has_no_publicly_accessible_constructor' tests/features/bio-idn-001/PersonRepositoryGovernanceTests.cs"

# --- AC03: identity lifecycle states are implemented ---
check "AC03 PersonStatus has exactly the eight states the Master Technical Specification names" \
  bash -c "for s in PreEnrolled Verified Active Suspended Expired FormerAlumni Revoked Archived; do grep -q \"    \$s,\" '$FEATURE/Domain/PersonStatus.cs' || exit 1; done"
check "AC03 wire strings match the spec's literal tokens (PRE_ENROLLED .. ARCHIVED)" \
  bash -c "grep -q 'PRE_ENROLLED' '$FEATURE/Domain/PersonStatus.cs' && grep -q 'FORMER_ALUMNI' '$FEATURE/Domain/PersonStatus.cs'"
check "AC03 a transition table is enforced, not a bare unchecked enum field" \
  bash -c "grep -q 'PersonLifecycleTransitions.IsAllowed' '$FEATURE/PersonService.cs' && grep -q 'InvalidStatusTransition' '$FEATURE/Domain/PersonError.cs'"
check "AC03 ARCHIVED is terminal - a test proves no transition is ever allowed out of it" \
  bash -c "grep -q 'Archived_is_terminal_no_transition_is_ever_allowed_out_of_it' tests/features/bio-idn-001/PersonServiceTests.cs"
check "AC03 both legal and illegal transitions are tested, not only the happy path" \
  bash -c "grep -q 'A_baseline_lifecycle_transition_is_allowed' tests/features/bio-idn-001/PersonServiceTests.cs && grep -q 'An_illegal_lifecycle_transition_is_rejected' tests/features/bio-idn-001/PersonServiceTests.cs"
check "AC03 status updates require expected_version (ADR-0001 Decision 3.2, CAS)" \
  bash -c "grep -q 'ConcurrencyConflict' '$FEATURE/PersonService.cs'"

# --- AC04: deletion is governed; operational history is not orphaned ---
check "AC04 no delete/remove/purge/erase method exists anywhere on the repository or service" \
  bash -c "! grep -v '^\s*///\|^\s*//' '$FEATURE/Domain/IPersonRepository.cs' '$FEATURE/PersonService.cs' | grep -qiE '\b(Delete|Remove|Purge|Erase)Async\b'"
check "AC04 a structural test proves the omission adversarially, not just by inspection" \
  bash -c "grep -q 'No_delete_method_exists_anywhere_on_the_repository_or_the_service' tests/features/bio-idn-001/PersonServiceTests.cs"
check "AC04 the only lifecycle exit is the governed, audited ARCHIVED transition (same CAS path as any status change)" \
  bash -c "grep -q 'Archived' '$FEATURE/Domain/Person.cs'"

# --- AC05: all mutations are audited (ADR-0003 Decision 7) ---
check "Decision 7 element 1: a per-aggregate lock exists, in its own singleton-safe class" \
  bash -c "test -f '$FEATURE/PersonLockRegistry.cs' && grep -q 'AcquireAsync' '$FEATURE/PersonLockRegistry.cs'"
check "the lock lives in its own class because PersonService must be Scoped (captive-dependency avoidance)" \
  bash -c "grep -q 'AddScoped<PersonService>' api/Biolog.Api/Modules/BioIdn001Module.cs && grep -q 'AddSingleton<PersonLockRegistry>' api/Biolog.Api/Modules/BioIdn001Module.cs"
check "Decision 7 element 2: the repository commit is the return statement (audit already happened above it)" \
  bash -c "grep -qE 'return await repository\.(CreateAsync|ReplaceAsync)\(' '$FEATURE/PersonService.cs'"
check "Decision 7 element 3: ExistsAsync verification on an ambiguous audit-write throw" \
  bash -c "grep -q 'ExistsAsync' '$FEATURE/Audit/IPersonAuditSink.cs' && grep -q 'ExistsAsync' '$FEATURE/Audit/InMemoryPersonAuditSink.cs' && grep -q 'auditSink.ExistsAsync' '$FEATURE/PersonService.cs'"
check "Decision 7 element 4: CancellationToken.None on every commit once the audit is confirmed" \
  bash -c "[ \"\$(grep -c 'repository\.\(CreateAsync\|ReplaceAsync\)([^)]*, CancellationToken\.None)' '$FEATURE/PersonService.cs')\" = 2 ]"
check "a same-person concurrency race is proven (BIO-TEN-001 round-3-shape reproduction)" \
  bash -c "grep -q 'Two_genuinely_concurrent_status_updates_of_the_same_person_never_produce_a_false_allowed_audit' tests/features/bio-idn-001/PersonAuditTests.cs"
check "audit sink failure never leaves a committed-but-unaudited record (register/status-change)" \
  bash -c "grep -q 'Register_is_never_committed_when_the_audit_sink_fails' tests/features/bio-idn-001/PersonAuditTests.cs && grep -q 'UpdateStatus_is_never_committed_when_the_audit_sink_fails' tests/features/bio-idn-001/PersonAuditTests.cs"
check "a cancellation arriving right after a confirmed audit write does not abort the commit" \
  bash -c "grep -q 'A_cancellation_arriving_right_after_the_audit_write_succeeds_does_not_abort_the_commit' tests/features/bio-idn-001/PersonAuditTests.cs"
check "the audit trail withholds PII field values, carrying only field names (privacy-by-design)" \
  bash -c "grep -q 'Register_never_carries_a_PII_value_in_the_audit_event_only_field_names_in_Withheld' tests/features/bio-idn-001/PersonAuditTests.cs"
check "the audit builder registers 'personal'/'sensitive' classification ranks but never 'secret'" \
  bash -c "grep -q '\[\"personal\"\]' '$FEATURE/Audit/PersonAuditEventBuilder.cs' && grep -q '\[\"sensitive\"\]' '$FEATURE/Audit/PersonAuditEventBuilder.cs' && ! grep -q '\[\"secret\"\]' '$FEATURE/Audit/PersonAuditEventBuilder.cs'"

# --- Developer test wiring ---
check "unit/audit/scope tests reference the feature project" \
  bash -c "grep -q 'Biolog.Features.PersonRegistry.csproj' tests/features/bio-idn-001/Biolog.Features.PersonRegistry.Tests.csproj"
check "feature-local integration tests exist and reference TestHost" \
  bash -c "test -f tests/features/bio-idn-001/PersonEndpointsIntegrationTests.cs && grep -q 'Microsoft.AspNetCore.TestHost' tests/features/bio-idn-001/Biolog.Features.PersonRegistry.Tests.csproj"
check "docs/features/bio-idn-001/README.md documents the feature" test -f docs/features/bio-idn-001/README.md
check "builder evidence recorded" test -d evidence/features/bio-idn-001/builder

echo "=============================="
echo "$pass passed, $fail failed"
[ "$fail" -eq 0 ]
