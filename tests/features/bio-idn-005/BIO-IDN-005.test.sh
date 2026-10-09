#!/usr/bin/env bash
# Offline-safe structural checks for BIO-IDN-005 (duplicate review foundation). Behavioural
# assertions (signal detection, human-audited decisions, no automatic merge, tenant isolation,
# audit atomicity, HTTP pipeline) live in the executable xunit suites under
# tests/features/bio-idn-005/ and
# tests/dotnet/Biolog.IntegrationTests/DuplicateReviewHostChannelTests.cs (see
# docs/features/bio-idn-005/README.md).
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"
pass=0; fail=0
check() { local label="$1"; shift; if "$@"; then echo "PASS  $label"; pass=$((pass+1)); else echo "FAIL  $label"; fail=$((fail+1)); fi; }

FEATURE=api/features/bio-idn-005

# --- AC01: candidate duplicates can arise from identifiers/contact data and later biometric signals ---
check "AC01 feature project exists" test -f "$FEATURE/Biolog.Features.DuplicateReview.csproj"
check "AC01 project targets the pinned .NET SDK's TFM" bash -c "grep -q '<TargetFramework>net10.0</TargetFramework>' '$FEATURE/Biolog.Features.DuplicateReview.csproj'"
check "AC01 DuplicateSignalSource names a name/DOB signal, a contact-data signal, an identifier signal, and a reserved future biometric signal" \
  bash -c "grep -q 'NameAndDateOfBirth' '$FEATURE/Domain/DuplicateSignalSource.cs' && grep -q 'ContactData' '$FEATURE/Domain/DuplicateSignalSource.cs' && grep -q 'InstitutionalIdentifierValue' '$FEATURE/Domain/DuplicateSignalSource.cs' && grep -q 'Biometric' '$FEATURE/Domain/DuplicateSignalSource.cs'"
check "AC01 module lives one-way in api/Biolog.Api/Modules, not back in the feature project" \
  bash -c "test -f api/Biolog.Api/Modules/BioIdn005Module.cs && ! test -f '$FEATURE/BioIdn005Module.cs'"
check "AC01 registered in the packaged Program.cs module array" \
  bash -c "grep -q 'new BioIdn005Module()' api/Biolog.Api/Program.cs"
check "AC01 a scan endpoint exists, nested under the owning person, versioned per ADR-0004 §1.1" \
  bash -c "grep -q '/api/v1/persons/{personId}/duplicate-candidates' '$FEATURE/Api/DuplicateCandidateEndpoints.cs'"
check "AC01 a test proves a candidate is raised from a shared name and date of birth" \
  bash -c "grep -q 'Scan_raises_a_candidate_for_two_people_sharing_the_same_name_and_date_of_birth' tests/features/bio-idn-005/DuplicateReviewServiceTests.cs"
check "AC01 a test proves a candidate is raised from a shared active institutional identifier value" \
  bash -c "grep -q 'Scan_raises_a_candidate_for_two_people_sharing_an_active_institutional_identifier_value' tests/features/bio-idn-005/DuplicateReviewServiceTests.cs"
check "AC01 a real-host test proves both signals through the real BIO-IDN-001/BIO-IDN-003-backed adapters" \
  bash -c "grep -q 'raises_a_candidate_from_two_real_persons_sharing_a_name_and_date_of_birth' tests/dotnet/Biolog.IntegrationTests/DuplicateReviewHostChannelTests.cs && grep -q 'raises_a_candidate_from_two_real_persons_sharing_a_real_institutional_identifier_value' tests/dotnet/Biolog.IntegrationTests/DuplicateReviewHostChannelTests.cs"

# --- BIO-IDN-005-QA-01 (Seyi round 1, High): AC01's contact-data signal was absent - name/DOB
# was mislabelled as "contact data" rather than a genuinely real contact-data source/signal ---
check "QA-01 a real, tenant-scoped PersonContactPoint aggregate exists with email and phone fields, owned entirely by this feature (no ProjectReference to another project)" \
  bash -c "grep -q 'public string? Email { get; init; }' '$FEATURE/Domain/PersonContactPoint.cs' && grep -q 'public string? Phone { get; init; }' '$FEATURE/Domain/PersonContactPoint.cs'"
check "QA-01 a set-contact-point endpoint exists, requiring an asserted actor" \
  bash -c "grep -q '/api/v1/persons/{personId}/contact-point' '$FEATURE/Api/PersonContactPointEndpoints.cs' && grep -q 'ActorResolver.Resolve' '$FEATURE/Api/PersonContactPointEndpoints.cs'"
check "QA-01 email is normalized (trimmed, lower-cased) at write time" \
  bash -c "grep -q 'ToLowerInvariant' '$FEATURE/PersonContactPointService.cs'"
check "QA-01 phone is normalized to digits-only at comparison time (deliberately not attempting international dialing-code reconciliation)" \
  bash -c "grep -q 'NormalizePhone' '$FEATURE/DuplicateReviewService.cs' && grep -q 'char.IsDigit' '$FEATURE/DuplicateReviewService.cs'"
check "QA-01 ScanForPersonAsync reads PersonContactPoint directly (no gateway needed - same-project aggregate) and raises ContactData on a match" \
  bash -c "grep -q 'contactPointRepository.GetAsync' '$FEATURE/DuplicateReviewService.cs' && grep -q 'SharesContactData' '$FEATURE/DuplicateReviewService.cs' && grep -q 'DuplicateSignalSource.ContactData' '$FEATURE/DuplicateReviewService.cs'"
check "QA-01 the existence gateway is checked before a contact point is ever stored (no orphan/cross-tenant contact records)" \
  bash -c "grep -q 'personGateway.GetAsync' '$FEATURE/PersonContactPointService.cs'"
check "QA-01 a test proves a candidate is raised from a shared email address" \
  bash -c "grep -q 'QA01_scan_raises_a_candidate_for_two_people_sharing_an_email_address' tests/features/bio-idn-005/DuplicateReviewServiceTests.cs"
check "QA-01 a test proves a candidate is raised from a shared phone number written differently (normalization boundary)" \
  bash -c "grep -q 'QA01_scan_raises_a_candidate_for_two_people_sharing_a_phone_number_written_differently' tests/features/bio-idn-005/DuplicateReviewServiceTests.cs"
check "QA-01 a test proves nonmatching contact data raises nothing" \
  bash -c "grep -q 'QA01_scan_raises_nothing_for_two_people_with_different_contact_data' tests/features/bio-idn-005/DuplicateReviewServiceTests.cs"
check "QA-01 a test proves two people with no contact data on file are never treated as a match" \
  bash -c "grep -q 'QA01_two_people_with_no_contact_data_on_file_are_never_treated_as_a_match' tests/features/bio-idn-005/DuplicateReviewServiceTests.cs"
check "QA-01 a test proves the contact point's own audit trail withholds the actual email/phone values" \
  bash -c "grep -q 'Audit_trail_withholds_the_actual_email_and_phone_values' tests/features/bio-idn-005/PersonContactPointServiceTests.cs"
check "QA-01 a real-host test proves the contact-data signal through the real endpoint and scan" \
  bash -c "grep -q 'BIOIDN005_QA01_scanning_through_a_verified_hostname_raises_a_candidate_from_two_real_persons_sharing_a_real_contact_data_value' tests/dotnet/Biolog.IntegrationTests/DuplicateReviewHostChannelTests.cs"
check "QA-01 a real-host test proves cross-tenant exclusion for the contact-data signal too, not only name/DOB" \
  bash -c "grep -q 'BIOIDN005_QA01_a_tenant_B_person_sharing_the_same_contact_data_never_surfaces_as_a_candidate_for_a_tenant_A_subject' tests/dotnet/Biolog.IntegrationTests/DuplicateReviewHostChannelTests.cs"
check "QA-01 governance: IPersonContactPointRepository/InMemoryPersonContactPointRepository are internal, and PersonContactPoint has no public constructor" \
  bash -c "grep -q '^internal interface IPersonContactPointRepository' '$FEATURE/Domain/IPersonContactPointRepository.cs' && grep -q 'IPersonContactPointRepository_and_InMemoryPersonContactPointRepository_are_not_public' tests/features/bio-idn-005/DuplicateCandidateRepositoryGovernanceTests.cs && grep -q 'PersonContactPoint_has_no_publicly_accessible_constructor' tests/features/bio-idn-005/DuplicateCandidateRepositoryGovernanceTests.cs"

# --- AC02: review decisions are human-audited ---
check "AC02 a decisions endpoint exists, requiring an asserted actor" \
  bash -c "grep -q '/decisions' '$FEATURE/Api/DuplicateCandidateEndpoints.cs' && grep -q 'ActorResolver.Resolve' '$FEATURE/Api/DuplicateCandidateEndpoints.cs'"
check "AC02 a decision requires a non-blank reason_code" \
  bash -c "grep -q 'ValidateReasonCode' '$FEATURE/DuplicateReviewService.cs'"
check "AC02 the decision carries the deciding actor and reason on the record itself" \
  bash -c "grep -q 'DecidedByActorId' '$FEATURE/Domain/DuplicateCandidate.cs' && grep -q 'DecisionReasonCode' '$FEATURE/Domain/DuplicateCandidate.cs'"
check "AC02 a test proves the decision records the actor and reason" \
  bash -c "grep -q 'Decide_records_a_confirmed_duplicate_decision_with_the_actor_and_reason' tests/features/bio-idn-005/DuplicateReviewServiceTests.cs"
check "AC02 a test proves a missing reason_code is rejected" \
  bash -c "grep -q 'Decide_rejects_a_missing_reason_code' tests/features/bio-idn-005/DuplicateReviewServiceTests.cs"
check "AC02 an audit test proves the decision is recorded as its own audit event carrying the reason code" \
  bash -c "grep -q 'Deciding_a_candidate_records_an_audit_event_carrying_the_reason_code' tests/features/bio-idn-005/DuplicateCandidateAuditTests.cs"

# --- AC03: merge is never automatic ---
check "AC03 DuplicateReviewStatus has no Merged member" \
  bash -c "! grep -v '^\s*///\|^\s*//' '$FEATURE/Domain/DuplicateReviewStatus.cs' | grep -q 'Merged,'"
check "AC03 no ProjectReference to BIO-IDN-001's project anywhere in this feature's own csproj (merge is structurally impossible, not merely undocumented)" \
  bash -c "! grep -q '<ProjectReference.*bio-idn-001' '$FEATURE/Biolog.Features.DuplicateReview.csproj'"
check "AC03 no method or endpoint anywhere in this feature is named anything like merge (code, not prose)" \
  bash -c "! grep -rv '^\s*///\|^\s*//' '$FEATURE' --include='*.cs' | grep -qi 'mergeasync\|mergeperson\|\bmerge(\b'"
check "AC03 a structural test proves this project holds no assembly reference to BIO-IDN-001's PersonRegistry" \
  bash -c "grep -q 'This_project_has_no_assembly_reference_to_BIO_IDN_001s_PersonRegistry_project' tests/features/bio-idn-005/DuplicateCandidateRepositoryGovernanceTests.cs"
check "AC03 a reflection test proves no method anywhere in this project is named anything like merge" \
  bash -c "grep -q 'No_type_in_this_project_defines_a_method_or_endpoint_named_anything_like_merge' tests/features/bio-idn-005/DuplicateCandidateRepositoryGovernanceTests.cs"

# --- AC04: cross-tenant duplicate search is prohibited by default ---
check "AC04 IPersonAttributeGateway/IIdentifierAttributeGateway wrap already tenant-scoped sibling services, never reimplementing tenant logic" \
  bash -c "grep -q 'internal interface IPersonAttributeGateway' '$FEATURE/Domain/IPersonAttributeGateway.cs' && grep -q 'internal interface IIdentifierAttributeGateway' '$FEATURE/Domain/IIdentifierAttributeGateway.cs'"
check "AC04 ScanForPersonAsync accepts no tenant parameter, header or flag that could widen a scan" \
  bash -c "! grep -q 'ScanForPersonAsync(string personId, string tenantId' '$FEATURE/DuplicateReviewService.cs'"
check "AC04 a missing or foreign-tenant subject is rejected as person_not_found (404), no enumeration oracle" \
  bash -c "grep -q 'PersonNotFound' '$FEATURE/Domain/DuplicateCandidateError.cs' && grep -A2 'PersonNotFound(string personId)' '$FEATURE/Domain/DuplicateCandidateError.cs' | grep -q ', 404,'"
check "the real adapters wrap BIO-IDN-001's/BIO-IDN-003's own tenant-scoped services (no reimplemented tenant logic)" \
  bash -c "grep -q 'PersonAttributeGatewayAdapter' api/Biolog.Api/Modules/BioIdn005Module.cs && grep -q 'personService.GetAsync\|personService.ListAsync' api/Biolog.Api/Modules/BioIdn005Module.cs && grep -q 'identifierService.ListAsync' api/Biolog.Api/Modules/BioIdn005Module.cs"
check "AC04 a real-host test proves a matching tenant-B person never surfaces as a candidate for a tenant-A subject" \
  bash -c "grep -q 'BIOIDN005_AC04_a_tenant_B_person_sharing_the_same_name_and_date_of_birth_never_surfaces_as_a_candidate_for_a_tenant_A_subject' tests/dotnet/Biolog.IntegrationTests/DuplicateReviewHostChannelTests.cs"
check "AC04 a real-host test proves an unrecognised host fails closed" \
  bash -c "grep -q 'Scanning_through_an_unrecognised_host_fails_closed' tests/dotnet/Biolog.IntegrationTests/DuplicateReviewHostChannelTests.cs"

# --- Never re-detects a decided pair ---
check "a rescan of a pair with an existing PendingReview status is the only augmentable state (decided pairs are left untouched)" \
  bash -c "grep -q 'existing.Status != DuplicateReviewStatus.PendingReview' '$FEATURE/DuplicateReviewService.cs'"
check "a test proves a rescan never reopens or changes a decided pair" \
  bash -c "grep -q 'A_rescan_never_reopens_or_changes_a_pair_a_human_already_decided' tests/features/bio-idn-005/DuplicateReviewServiceTests.cs"
check "a test proves signals accumulate across scans without losing earlier evidence" \
  bash -c "grep -q 'A_second_scan_augments_an_existing_pending_candidate_with_a_newly_observed_signal_without_losing_the_first' tests/features/bio-idn-005/DuplicateReviewServiceTests.cs"

# --- No canonical id of its own kind; natural key is the ordered pair ---
check "DuplicateCandidate has no canonical id of its own; natural key is (TenantId, PersonIdLow, PersonIdHigh)" \
  bash -c "grep -q 'public string PersonIdLow { get; }' '$FEATURE/Domain/DuplicateCandidate.cs' && grep -q 'public string PersonIdHigh { get; }' '$FEATURE/Domain/DuplicateCandidate.cs'"
check "OrderPair canonicalizes an unordered pair so {a,b} and {b,a} resolve to one record" \
  bash -c "grep -q 'public static (string Low, string High) OrderPair' '$FEATURE/Domain/DuplicateCandidate.cs'"
check "a test proves OrderPair is symmetric" \
  bash -c "grep -q 'OrderPair_produces_the_same_result_regardless_of_input_order' tests/features/bio-idn-005/DuplicateCandidateTests.cs"
check "a test proves the pair may be decided in either order" \
  bash -c "grep -q 'Decide_accepts_the_pair_named_in_either_order' tests/features/bio-idn-005/DuplicateReviewServiceTests.cs"

# --- Governance, applying BIO-IDN-001-QA-01's lesson from the start ---
check "governance: IDuplicateCandidateRepository and InMemoryDuplicateCandidateRepository are internal, not public" \
  bash -c "grep -q '^internal interface IDuplicateCandidateRepository' '$FEATURE/Domain/IDuplicateCandidateRepository.cs' && grep -q '^internal sealed class InMemoryDuplicateCandidateRepository' '$FEATURE/Domain/InMemoryDuplicateCandidateRepository.cs'"
check "governance: DuplicateCandidate's constructor is internal, not a public object-initializer surface" \
  bash -c "grep -q '    internal DuplicateCandidate(string tenantId, string personIdLow, string personIdHigh)' '$FEATURE/Domain/DuplicateCandidate.cs'"
check "governance: identity fields are get-only - immutable even via a 'with' expression" \
  bash -c "grep -q 'public string TenantId { get; }' '$FEATURE/Domain/DuplicateCandidate.cs' && grep -q 'public string PersonIdLow { get; }' '$FEATURE/Domain/DuplicateCandidate.cs' && grep -q 'public string PersonIdHigh { get; }' '$FEATURE/Domain/DuplicateCandidate.cs'"
check "governance: DuplicateReviewService itself is internal too" \
  bash -c "grep -q '^internal sealed class DuplicateReviewService' '$FEATURE/DuplicateReviewService.cs'"
check "governance: the InternalsVisibleTo seam is explicit and auditable in source control" \
  bash -c "grep -q '<InternalsVisibleTo Include=\"Biolog.Api\" />' '$FEATURE/Biolog.Features.DuplicateReview.csproj' && grep -q '<InternalsVisibleTo Include=\"Biolog.Features.DuplicateReview.Tests\" />' '$FEATURE/Biolog.Features.DuplicateReview.csproj'"
check "governance: the repository independently re-validates identity immutability and initial state at the persistence boundary" \
  bash -c "grep -q 'IdentityImmutable' '$FEATURE/Domain/InMemoryDuplicateCandidateRepository.cs' && grep -q 'InvalidInitialState' '$FEATURE/Domain/InMemoryDuplicateCandidateRepository.cs'"
check "governance: a reflection test proves the surface is inaccessible to ordinary production consumers" \
  bash -c "grep -q 'IDuplicateCandidateRepository_and_InMemoryDuplicateCandidateRepository_are_not_public' tests/features/bio-idn-005/DuplicateCandidateRepositoryGovernanceTests.cs && grep -q 'DuplicateCandidate_has_no_publicly_accessible_constructor' tests/features/bio-idn-005/DuplicateCandidateRepositoryGovernanceTests.cs"

# --- Tenant boundary is mandatory, inherited from every sibling story's own pattern ---
check "the service reads the resolved tenant, never a caller-supplied id" \
  bash -c "[ \"\$(grep -c 'RequireTenant()' '$FEATURE/DuplicateReviewService.cs')\" -ge 2 ]"
check "a missing tenant context fails closed (tenant_context_missing, 400)" \
  bash -c "grep -q 'TenantContextMissing' '$FEATURE/Domain/DuplicateCandidateError.cs' && grep -A2 'TenantContextMissing()' '$FEATURE/Domain/DuplicateCandidateError.cs' | grep -q ', 400)'"
check "the repository asserts a record's own tenant_id on every read/write" \
  bash -c "grep -q 'current.TenantId != expected.TenantId' '$FEATURE/Domain/InMemoryDuplicateCandidateRepository.cs'"
check "real shipped-host positive-path test exists (ADR-0002 Decision 6 obligation)" \
  bash -c "test -f tests/dotnet/Biolog.IntegrationTests/DuplicateReviewHostChannelTests.cs && grep -q 'WebApplicationFactory<Program>' tests/dotnet/Biolog.IntegrationTests/DuplicateReviewHostChannelTests.cs"

# --- ADR-0003 Decision 7: audit-atomicity ---
check "Decision 7 element 1: a per-pair lock exists in its own singleton-safe class" \
  bash -c "test -f '$FEATURE/DuplicateCandidateLockRegistry.cs' && grep -q 'AcquireAsync' '$FEATURE/DuplicateCandidateLockRegistry.cs'"
check "the lock lives in its own class because the service must be Scoped (captive-dependency avoidance)" \
  bash -c "grep -q 'AddScoped<DuplicateReviewService>' api/Biolog.Api/Modules/BioIdn005Module.cs && grep -q 'AddSingleton<DuplicateCandidateLockRegistry>' api/Biolog.Api/Modules/BioIdn005Module.cs"
check "Decision 7 element 2: every repository commit is the return statement (audit already happened above it)" \
  bash -c "grep -qE 'return await repository\.(CreateAsync|ReplaceAsync)\(' '$FEATURE/DuplicateReviewService.cs'"
check "Decision 7 element 3: ExistsAsync verification on an ambiguous audit-write throw" \
  bash -c "grep -q 'ExistsAsync' '$FEATURE/Audit/IDuplicateCandidateAuditSink.cs' && grep -q 'auditSink.ExistsAsync' '$FEATURE/DuplicateReviewService.cs'"
check "Decision 7 element 4: CancellationToken.None on every commit once the audit is confirmed (raise, augment, decide)" \
  bash -c "[ \"\$(grep -c 'repository\.\(CreateAsync\|ReplaceAsync\)([^)]*, CancellationToken\.None)' '$FEATURE/DuplicateReviewService.cs')\" = 3 ]"
check "a same-pair concurrency race is proven, never producing two candidate records" \
  bash -c "grep -q 'Two_genuinely_concurrent_scans_of_the_same_pair_never_produce_two_candidate_records' tests/features/bio-idn-005/DuplicateCandidateAuditTests.cs"
check "audit sink failure never leaves a committed-but-unaudited record (raise/decide)" \
  bash -c "grep -q 'Raising_a_candidate_is_never_committed_when_the_audit_sink_fails' tests/features/bio-idn-005/DuplicateCandidateAuditTests.cs && grep -q 'Deciding_is_never_committed_when_the_audit_sink_fails' tests/features/bio-idn-005/DuplicateCandidateAuditTests.cs"
check "lock scope was reasoned explicitly, not assumed by analogy (BIO-IDN-003-QA-01's lesson)" \
  bash -c "grep -q 'reasoned explicitly, not assumed by analogy' '$FEATURE/DuplicateCandidateLockRegistry.cs'"

# --- Audit trail privacy: no compared PII value ever enters Detail ---
check "the audit event builder registers only public/internal - never personal/sensitive/secret (no PII value is ever passed to it)" \
  bash -c "grep -q '\[\"public\"\] = 0' '$FEATURE/Audit/DuplicateCandidateAuditEventBuilder.cs' && grep -q '\[\"internal\"\] = 1' '$FEATURE/Audit/DuplicateCandidateAuditEventBuilder.cs' && ! grep -v '^\s*///\|^\s*//' '$FEATURE/Audit/DuplicateCandidateAuditEventBuilder.cs' | grep -qE '\[\"(personal|sensitive|secret)\"\]'"
check "a test proves no compared PII value (name/date of birth) ever appears in the audit trail" \
  bash -c "grep -q 'Raising_a_candidate_records_exactly_one_allowed_audit_event_naming_the_matched_signal_but_no_PII_value' tests/features/bio-idn-005/DuplicateCandidateAuditTests.cs"

# --- Developer test wiring ---
check "unit/audit/scope tests reference the feature project" \
  bash -c "grep -q 'Biolog.Features.DuplicateReview.csproj' tests/features/bio-idn-005/Biolog.Features.DuplicateReview.Tests.csproj"
check "feature-local integration tests exist and reference TestHost" \
  bash -c "test -f tests/features/bio-idn-005/DuplicateCandidateEndpointsIntegrationTests.cs && grep -q 'Microsoft.AspNetCore.TestHost' tests/features/bio-idn-005/Biolog.Features.DuplicateReview.Tests.csproj"
check "docs/features/bio-idn-005/README.md documents the feature" test -f docs/features/bio-idn-005/README.md
check "builder evidence recorded" test -d evidence/features/bio-idn-005/builder

echo "=============================="
echo "$pass passed, $fail failed"
[ "$fail" -eq 0 ]
