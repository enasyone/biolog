#!/usr/bin/env bash
# Offline-safe structural checks for BIO-CRD-005 (physical card issue transaction).
# Behavioural assertions live in the executable suites: the backend's own xunit tests
# (tests/features/bio-crd-005/) and the real-packaged-host tests
# (tests/dotnet/Biolog.IntegrationTests/CardIssuanceHostChannelTests.cs) - see
# docs/features/bio-crd-005/README.md. This story has no frontend UI - no AC asks for one,
# matching BIO-CRD-001's own backend-only precedent.
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"
pass=0; fail=0
check() { local label="$1"; shift; if "$@"; then echo "PASS  $label"; pass=$((pass+1)); else echo "FAIL  $label"; fail=$((fail+1)); fi; }

API_FEATURE=api/features/bio-crd-005
API_TESTS=tests/features/bio-crd-005
HOST_TESTS=tests/dotnet/Biolog.IntegrationTests

# --- AC01/AC02: sequence starts 001, increments permanently, never reused or reset ---
check "AC01 sequence numbers start at 1, computed per (tenant, person, card class) as a pure read the caller's own lock serialises against a commit" \
  bash -c "grep -q 'existing.Count == 0 ? 1 : existing\[\^1\].SequenceNumber + 1' $API_FEATURE/Domain/ICardIssueRepository.cs"
check "AC01/BIO-CRD-005-QA-01 sequence assignment (NextSequenceNumber) and commit (Create) are two separate calls, so the audit can run between them" bash -c \
  "grep -q 'int NextSequenceNumber' $API_FEATURE/Domain/ICardIssueRepository.cs && grep -q 'CardIssue Create(CardIssue issue)' $API_FEATURE/Domain/ICardIssueRepository.cs"
check "AC01 the wire response renders the zero-padded 001/002/003 display form ADR-0001 §1.6 names verbatim" \
  bash -c "grep -q 'SequenceDisplay = issue.SequenceNumber.ToString(\"D3\")' $API_FEATURE/Api/CardIssueDtos.cs"
check "AC02 no delete operation exists anywhere on the repository (governed deletion, no reuse/reset)" \
  bash -c "! grep -qE 'Delete|Remove' $API_FEATURE/Domain/ICardIssueRepository.cs"
check "a test proves the sequence increments across three issued cards and the earliest row is never overwritten" \
  bash -c "grep -q 'AC01_each_append_increments_the_sequence_and_AC02_never_reuses_or_resets_it' $API_TESTS/CardIssueRepositoryTests.cs"
check "a test proves concurrent issuance for the same (person, card class) never collides or skips a sequence number (service-level, via the lock)" \
  bash -c "grep -q 'Concurrent_service_level_issuance' $API_TESTS/CardIssueAuditAtomicityTests.cs"
check "BIO-CRD-005-QA-01 regression: a confirmed audit failure during issuance leaves no card issue committed" \
  bash -c "grep -q 'BIOCRD005QA01_a_confirmed_audit_failure_during_issuance_leaves_no_card_issue_committed' $API_TESTS/CardIssueAuditAtomicityTests.cs"
check "BIO-CRD-005-QA-01: IssueAsync calls AuditAsync strictly before repository.Create (audit-before-commit, ADR-0003 Decision 7)" bash -c \
  "audit_line=\$(grep -n 'await AuditAsync' $API_FEATURE/CardIssueService.cs | head -1 | cut -d: -f1);
   create_line=\$(grep -n 'return repository.Create(issue);' $API_FEATURE/CardIssueService.cs | head -1 | cut -d: -f1);
   [ -n \"\$audit_line\" ] && [ -n \"\$create_line\" ] && [ \"\$audit_line\" -lt \"\$create_line\" ]"

# --- AC03: encode/print/verify steps may precede activation ---
check "encode/print/verify are independent, set-once timestamp fields, not a single ordered enum" bash -c \
  "grep -q 'string? EncodedAt' $API_FEATURE/Domain/CardIssue.cs &&
   grep -q 'string? PrintedAt' $API_FEATURE/Domain/CardIssue.cs &&
   grep -q 'string? VerifiedAt' $API_FEATURE/Domain/CardIssue.cs"
check "re-recording an already-recorded step is refused, not silently overwritten" \
  bash -c "grep -q 'StepAlreadyRecorded' $API_FEATURE/Domain/CardIssueError.cs"
check "a test proves encode/print/verify may all precede a still-succeeding activation" \
  bash -c "grep -q 'AC03_encode_print_and_verify_are_independent_and_may_all_precede_activation' $API_TESTS/CardIssueEndpointsIntegrationTests.cs"
check "a test proves activation succeeds even when no encode/print/verify step was ever recorded (AC03: may, not must)" \
  bash -c "grep -q 'AC03_activation_succeeds_even_when_no_encode_print_or_verify_step_was_ever_recorded' $API_TESTS/CardIssueEndpointsIntegrationTests.cs"

# --- AC04: activation is transactional and prevents two active physical cards ---
check "CardIssue carries a real ADR-0001 §3.1/3.2 CAS version - a genuinely mutable aggregate, unlike this epic's append-only records" \
  bash -c "grep -q 'required int Version' $API_FEATURE/Domain/CardIssue.cs"
check "absent expected_version is 428 precondition_required; a stale one is 409 concurrency_conflict, per ADR-0001 §3.2's table exactly" bash -c \
  "grep -q 'PreconditionRequired' $API_FEATURE/Domain/CardIssueError.cs && grep -q 'ConcurrencyConflict' $API_FEATURE/Domain/CardIssueError.cs"
check "activation is refused, never silently superseding, when a sibling is already active" \
  bash -c "grep -q 'AnotherCardAlreadyActive' $API_FEATURE/Domain/CardIssueError.cs"
check "activation and every other mutation is serialised by a per-(tenant, person, card class) lock, making the check-then-set genuinely transactional" \
  bash -c "grep -q 'CardIssueLockRegistry' $API_FEATURE/CardIssueService.cs"
check "activation requires Idempotency-Key (ADR-0001 §3.4 / ADR-0004 §4.3 names card activation explicitly)" \
  bash -c "grep -q 'IdempotencyKeyRequired' $API_FEATURE/CardIssueService.cs"
check "a test proves two concurrent activation attempts for the same (person, card class) never both succeed" \
  bash -c "grep -q 'Concurrent_activation_attempts' $API_TESTS/CardIssueEndpointsIntegrationTests.cs"
check "a test proves replaying the same activation idempotency key returns the original result without re-executing" \
  bash -c "grep -q 'AC04_replaying_the_same_activation_idempotency_key' $API_TESTS/CardIssueEndpointsIntegrationTests.cs"
check "a test proves reusing an idempotency key with different content is rejected" \
  bash -c "grep -q 'AC04_reusing_an_idempotency_key_with_different_request_content_is_rejected' $API_TESTS/CardIssueEndpointsIntegrationTests.cs"

# --- AC05: provenance records template/photo/logo/signature/operator/printer/time ---
check "CardIssueProvenance carries every named field: template/photo/logo/signature/operator/printer/time" bash -c \
  "grep -q 'TemplateRef' $API_FEATURE/Domain/CardIssueProvenance.cs &&
   grep -q 'PhotoRef' $API_FEATURE/Domain/CardIssueProvenance.cs &&
   grep -q 'LogoRef' $API_FEATURE/Domain/CardIssueProvenance.cs &&
   grep -q 'SignatureRef' $API_FEATURE/Domain/CardIssueProvenance.cs &&
   grep -q 'Operator' $API_FEATURE/Domain/CardIssueProvenance.cs &&
   grep -q 'PrinterRef' $API_FEATURE/Domain/CardIssueProvenance.cs &&
   grep -q 'IssuedAt' $API_FEATURE/Domain/CardIssueProvenance.cs"
check "operator is a structured actor, never a bare string (ADR-0003)" \
  bash -c "grep -q 'Audit.AuditActor Operator' $API_FEATURE/Domain/CardIssueProvenance.cs"
check "provenance is captured once at issuance and carries no update operation (immutable snapshot)" \
  bash -c "! grep -qE 'SetProvenance|UpdateProvenance' $API_FEATURE/CardIssueService.cs"
check "a test proves every provenance field round-trips through the real HTTP response" \
  bash -c "grep -q 'AC05_provenance_records_template_photo_logo_signature_operator_printer_and_time' $API_TESTS/CardIssueEndpointsIntegrationTests.cs"

# --- ADR-0001 identifier-registry discipline ---
check "no canonical id of its own kind minted for a physical card issue (ADR-0001: exactly six registered types)" \
  bash -c "! grep -qE 'crd_[a-z]|CardIssuePrefix' $API_FEATURE/Domain/CardIssue.cs"
check "the story's own header cites ADR-0001's own anticipation of this exact shape (\"a physical card issue is a child record, not this id\")" \
  bash -c "grep -q 'physical card issue is a child record, not this id' $API_FEATURE/Domain/CardIssue.cs"

# --- Module wiring and write-scope discipline ---
check "no ProjectReference to any other feature project, including bio-idn-001 (a genuine runtime dependency reached only through this project's own gateway interface)" \
  bash -c "! grep -q '<ProjectReference' $API_FEATURE/Biolog.Features.CardIssuance.csproj"
check "IPersonExistenceGateway is this project's own interface, not a reference to bio-idn-001's types" \
  bash -c "test -f $API_FEATURE/Domain/IPersonExistenceGateway.cs"
check "backend module lives one-way in api/Biolog.Api/Modules, not back in the feature project" \
  bash -c "test -f api/Biolog.Api/Modules/BioCrd005Module.cs && ! test -f '$API_FEATURE/BioCrd005Module.cs'"
check "registered in the packaged Program.cs module array, depending on Tenant and PersonRegistry" \
  bash -c "grep -q 'new BioCrd005Module()' api/Biolog.Api/Program.cs && grep -q '\"Tenant\", \"PersonRegistry\"' api/Biolog.Api/Modules/BioCrd005Module.cs"

# --- Developer test wiring ---
check "backend unit/integration tests exist" bash -c \
  'test -f tests/features/bio-crd-005/CardIssueRepositoryTests.cs &&
   test -f tests/features/bio-crd-005/CardIssueEndpointsIntegrationTests.cs'
check "a real-packaged-host test exists proving the genuine BIO-IDN-001 dependency through the shipped host channel" \
  test -f "$HOST_TESTS/CardIssuanceHostChannelTests.cs"
check "the host-channel test proves cross-tenant person-reference rejection is identical to the unknown-person case (ADR-0001 §1.5, no enumeration oracle)" \
  bash -c "grep -q 'BIOCRD005_a_tenant_B_caller_cannot_issue_a_card_for_a_real_tenant_A_person' $HOST_TESTS/CardIssuanceHostChannelTests.cs"
# --- TD-CRD-005-IDEMPOTENCY-RACE (High, Kemi-confirmed 2026-10-06) ---
check "the idempotency claim carries an explicit IsNewClaim ownership signal, not content-only State" \
  bash -c "grep -q 'IsNewClaim' $API_FEATURE/Domain/IdempotencyStore.cs"
check "ActivateAsync branches on IsNewClaim, not on State alone" \
  bash -c "grep -q 'claim.IsNewClaim' $API_FEATURE/CardIssueService.cs"
check "a deterministic two-caller concurrency test exists proving exactly one execution" \
  test -f $API_TESTS/TdIdempotencyRaceTests.cs
check "the regression test forces genuine claim-boundary contention (a barrier-gated store), not just a shared start gate" \
  bash -c "grep -q 'ClaimBarrierIdempotencyStore' $API_TESTS/TdIdempotencyRaceTests.cs"
check "a mutant-discrimination test proves the historical State-only check violates the same invariant the real fix satisfies" \
  bash -c "grep -q 'Mutant_discrimination' $API_TESTS/TdIdempotencyRaceTests.cs"
check "the one representative bounded-timeout test for the whole TD exists here" \
  bash -c "grep -q 'idempotency_wait_timeout' $API_TESTS/TdIdempotencyRaceTests.cs"

check "builder evidence recorded" test -d evidence/BIO-CRD-005/builder

echo "=============================="
echo "$pass passed, $fail failed"
[ "$fail" -eq 0 ]
