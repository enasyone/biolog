#!/usr/bin/env bash
# Offline-safe structural checks for BIO-CRD-007 (card production jobs and print/encode
# verification). Behavioural assertions live in the executable suites: the backend's own xunit
# tests (tests/features/bio-crd-007/) and the real-packaged-host tests
# (tests/dotnet/Biolog.IntegrationTests/CardProductionHostChannelTests.cs) - see
# docs/features/bio-crd-007/README.md. This story has no frontend UI - no AC asks for one,
# matching BIO-CRD-001/005's own backend-only precedent.
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"
pass=0; fail=0
check() { local label="$1"; shift; if "$@"; then echo "PASS  $label"; pass=$((pass+1)); else echo "FAIL  $label"; fail=$((fail+1)); fi; }

API_FEATURE=api/features/bio-crd-007
API_TESTS=tests/features/bio-crd-007
HOST_TESTS=tests/dotnet/Biolog.IntegrationTests

# --- AC01: production job lifecycle is implemented ---
check "AC01 a job status enum exists with InProgress/Passed/Failed/Rejected/Cancelled" \
  bash -c "grep -q 'InProgress,' $API_FEATURE/Domain/ProductionJobStatus.cs && grep -q 'Passed,' $API_FEATURE/Domain/ProductionJobStatus.cs && grep -q 'Rejected,' $API_FEATURE/Domain/ProductionJobStatus.cs && grep -q 'Cancelled,' $API_FEATURE/Domain/ProductionJobStatus.cs"
check "AC01 job sequence numbers start at 1 and never reused - the same pure-read-then-Create split BIO-CRD-005/006 already established" \
  bash -c "grep -q 'existing.Count == 0 ? 1 : existing\[\^1\].JobSequenceNumber + 1' $API_FEATURE/Domain/IProductionJobRepository.cs"
check "AC01 job status is derived from all three step outcomes, never set directly by a caller" \
  bash -c "grep -q 'ResolveStatus' $API_FEATURE/ProductionService.cs"
check "a test proves all three passing steps resolve the job to Passed" \
  bash -c "grep -q 'All_three_steps_passing_resolves_the_job_to_Passed' $API_TESTS/ProductionJobLifecycleTests.cs"
check "a test proves any one failing step resolves the job to Failed even if the others passed" \
  bash -c "grep -q 'Any_one_step_failing_resolves_the_job_to_Failed_even_if_the_others_passed' $API_TESTS/ProductionJobLifecycleTests.cs"
check "a test proves a job can be explicitly cancelled" \
  bash -c "grep -q 'CancelJob_closes_an_InProgress_job' $API_TESTS/ProductionJobLifecycleTests.cs"

# --- AC02: print/encode/read-back/QC failures do not activate a credential ---
check "AC02 activation is gated - ActivateAfterProductionAsync exists and checks AllowsActivation before any delegate call" \
  bash -c "grep -q 'ActivateAfterProductionAsync' $API_FEATURE/ProductionService.cs && grep -q 'AllowsActivation' $API_FEATURE/ProductionService.cs"
check "AC02 only Passed allows activation - not InProgress, Failed, Rejected or Cancelled" \
  bash -c "grep -q 'status == ProductionJobStatus.Passed' $API_FEATURE/Domain/ProductionJobStatus.cs"
check "a test proves activation is refused when no production job has ever been created" \
  bash -c "grep -q 'Activation_is_refused_when_no_production_job_has_ever_been_created' $API_TESTS/ProductionActivationGateTests.cs"
check "a test proves activation is refused when the latest job Failed" \
  bash -c "grep -q 'Activation_is_refused_when_the_latest_job_Failed' $API_TESTS/ProductionActivationGateTests.cs"
check "a test proves activation is refused when the latest job was explicitly Rejected even though it mechanically passed" \
  bash -c "grep -q 'Activation_is_refused_when_the_latest_job_was_Rejected_even_though_it_mechanically_Passed' $API_TESTS/ProductionActivationGateTests.cs"
check "a test proves the real card issue stays untouched (never Active) when a gated activation is refused" \
  bash -c "grep -q 'AC02_activation_through_the_real_host_is_refused_when_no_production_job_has_passed_and_the_real_card_stays_Issued' $HOST_TESTS/CardProductionHostChannelTests.cs"

# --- AC03: signed, time-limited production packages support permitted offline mode ---
check "AC03 packages are signed with HMAC-SHA256 using a tenant-scoped key, never a shared or hardcoded secret" \
  bash -c "grep -q 'HMACSHA256' $API_FEATURE/Domain/ProductionPackageSigner.cs && grep -q 'RandomNumberGenerator.GetBytes' $API_FEATURE/Domain/ProductionPackageSigner.cs"
check "AC03 signature verification uses constant-time comparison" \
  bash -c "grep -q 'CryptographicOperations.FixedTimeEquals' $API_FEATURE/Domain/ProductionPackageSigner.cs"
check "AC03 packages carry an explicit expiry and completion checks it" \
  bash -c "grep -q 'ExpiresAt' $API_FEATURE/Domain/ProductionPackage.cs && grep -q 'PackageExpired' $API_FEATURE/ProductionService.cs"
check "AC03 a package can only ever be consumed once" \
  bash -c "grep -q 'PackageAlreadyConsumed' $API_FEATURE/ProductionService.cs"
check "AC03 offline-complete requires Idempotency-Key (ADR-0004 §4.3 names offline sync intake explicitly)" \
  bash -c "grep -q 'IdempotencyKeyRequired' $API_FEATURE/ProductionService.cs"
check "AC03 the consumed-check and job creation happen under the same per-card lock that serialises every other mutation on this card, closing the concurrent-double-consumption race" bash -c \
  "lock_line=\$(grep -n 'AcquireAsync(tenantId, payload.PersonId, payload.CardClass, payload.SequenceNumber' $API_FEATURE/ProductionService.cs | head -1 | cut -d: -f1);
   consumed_line=\$(grep -n 'storedPackage.Consumed' $API_FEATURE/ProductionService.cs | head -1 | cut -d: -f1);
   [ -n \"\$lock_line\" ] && [ -n \"\$consumed_line\" ] && [ \"\$lock_line\" -lt \"\$consumed_line\" ]"
check "a test proves a tampered token is rejected" \
  bash -c "grep -q 'CompleteOfflineJob_rejects_a_tampered_token' $API_TESTS/ProductionPackageTests.cs"
check "a test proves an expired package is rejected" \
  bash -c "grep -q 'CompleteOfflineJob_rejects_an_expired_package' $API_TESTS/ProductionPackageTests.cs"
check "a test proves a package cannot be consumed twice" \
  bash -c "grep -q 'CompleteOfflineJob_rejects_the_same_package_being_submitted_twice_under_different_idempotency_keys' $API_TESTS/ProductionPackageTests.cs"
check "a test proves a token signed for a different tenant is rejected" \
  bash -c "grep -q 'CompleteOfflineJob_rejects_a_token_signed_for_a_different_tenant' $API_TESTS/ProductionPackageTests.cs"
check "a real-packaged-host test proves the full offline package round trip produces and activates a real card" \
  bash -c "grep -q 'AC03_a_signed_package_issued_and_completed_through_the_real_host_produces_and_activates_a_real_card' $HOST_TESTS/CardProductionHostChannelTests.cs"

# --- AC04: production report records printer/operator/workstation/card identifiers/result/rejection/reprint reason ---
check "AC04 a job records printer, a structured operator actor, and workstation" \
  bash -c "grep -q 'PrinterRef' $API_FEATURE/Domain/ProductionJob.cs && grep -q 'AuditActor Operator' $API_FEATURE/Domain/ProductionJob.cs && grep -q 'WorkstationRef' $API_FEATURE/Domain/ProductionJob.cs"
check "AC04 a reprint (job sequence > 1) requires a recorded reason; a first attempt must not carry one" \
  bash -c "grep -q 'ReprintReasonRequired' $API_FEATURE/Domain/ProductionError.cs && grep -q 'ReprintReasonNotAllowed' $API_FEATURE/Domain/ProductionError.cs"
check "AC04 a rejection records its own reason" \
  bash -c "grep -q 'RejectionReason' $API_FEATURE/Domain/ProductionJob.cs"
check "AC04 the card issue's own identifiers (person/card class/sequence) are the job's own natural key, never a separate copy that could drift" \
  bash -c "grep -q 'TenantId, PersonId, CardClass, SequenceNumber' $API_FEATURE/Domain/ProductionJob.cs"
check "a test proves a reprint without a reason is refused and a reprint with one is recorded" \
  bash -c "grep -q 'A_reprint_after_cancellation_can_proceed_and_requires_a_reprint_reason' $API_TESTS/ProductionJobLifecycleTests.cs"
check "a test proves ListJobsAsync returns the full production report for a card" \
  bash -c "grep -q 'ListJobsAsync_returns_the_full_production_report_for_a_card' $API_TESTS/ProductionJobLifecycleTests.cs"

# --- AC05: activation preserves one-active-card invariant ---
check "AC05 activation delegates to BIO-CRD-005's own real, authoritative ActivateAsync - never a competing local implementation of the invariant" \
  bash -c "grep -q 'return await cardIssueGateway.ActivateAsync' $API_FEATURE/ProductionService.cs"
check "a test proves BIO-CRD-005's own one-active-card invariant still refuses a second activation even after this story's own gate passes" \
  bash -c "grep -q 'AC05_the_real_gateways_own_one_active_card_invariant_still_refuses_a_second_activation' $API_TESTS/ProductionActivationGateTests.cs"

# --- ADR-0001 identifier-registry discipline ---
check "no new canonical identifier type minted - a job reuses BIO-CRD-005's own natural composite key plus this project's own job sequence number" \
  bash -c "! grep -qE 'crd_[a-z]|ProductionJobPrefix' $API_FEATURE/Domain/ProductionJob.cs"
check "a production package id is disclosed as an opaque bearer token, not a canonical identifier of its own kind" \
  bash -c "grep -q 'NOT a canonical BIOLOG identifier' $API_FEATURE/Domain/ProductionPackage.cs"

# --- Module wiring and write-scope discipline ---
check "no ProjectReference to any other feature project, including bio-crd-005 (a genuine runtime dependency reached only through this project's own gateway interface)" \
  bash -c "! grep -q '<ProjectReference' $API_FEATURE/Biolog.Features.CardProduction.csproj"
check "ICardIssueGateway is this project's own interface, not a reference to bio-crd-005's types" \
  test -f "$API_FEATURE/Domain/ICardIssueGateway.cs"
check "backend module lives one-way in api/Biolog.Api/Modules, not back in the feature project" \
  bash -c "test -f api/Biolog.Api/Modules/BioCrd007Module.cs && ! test -f '$API_FEATURE/BioCrd007Module.cs'"
check "registered in the packaged Program.cs module array, depending on Tenant and CardIssuance" \
  bash -c "grep -q 'new BioCrd007Module()' api/Biolog.Api/Program.cs && grep -q '\"Tenant\", \"CardIssuance\"' api/Biolog.Api/Modules/BioCrd007Module.cs"
check "the module adapter translates BIO-CRD-005's own CardIssueError into this feature's own ProductionError, never leaking a sibling's exception type" \
  bash -c "grep -q 'catch (Biolog.Features.CardIssuance.Domain.CardIssueError error)' api/Biolog.Api/Modules/BioCrd007Module.cs"

# --- Developer test wiring ---
check "backend unit/integration tests exist" bash -c \
  'test -f tests/features/bio-crd-007/ProductionJobRepositoryTests.cs &&
   test -f tests/features/bio-crd-007/ProductionJobLifecycleTests.cs &&
   test -f tests/features/bio-crd-007/ProductionActivationGateTests.cs &&
   test -f tests/features/bio-crd-007/ProductionPackageTests.cs &&
   test -f tests/features/bio-crd-007/ProductionEndpointsIntegrationTests.cs'
check "a real-packaged-host test exists proving the genuine BIO-CRD-005 dependency through the shipped host channel" \
  test -f "$HOST_TESTS/CardProductionHostChannelTests.cs"
# --- TD-CRD-007-IDEMPOTENCY-RACE (High, Kemi-confirmed 2026-10-06) ---
check "the idempotency claim carries an explicit IsNewClaim ownership signal, not content-only State" \
  bash -c "grep -q 'IsNewClaim' $API_FEATURE/Domain/IdempotencyStore.cs"
check "CompleteOfflineJobAsync branches on IsNewClaim, not on State alone" \
  bash -c "grep -q 'claim.IsNewClaim' $API_FEATURE/ProductionService.cs"
check "a deterministic two-caller concurrency test exists proving exactly one execution, distinct from SeyiRound3's different-key probe" \
  test -f $API_TESTS/TdIdempotencyRaceTests.cs
check "the regression test forces genuine claim-boundary contention (a barrier-gated store), not just a shared start gate" \
  bash -c "grep -q 'ClaimBarrierIdempotencyStore' $API_TESTS/TdIdempotencyRaceTests.cs"
check "a mutant-discrimination test proves the historical State-only check violates the same invariant the real fix satisfies" \
  bash -c "grep -q 'Mutant_discrimination' $API_TESTS/TdIdempotencyRaceTests.cs"

# --- BIO-OPS-003-AC04 round 2 (SY-OPS003-02, Kemi's ruling 2026-10-07): shared cross-feature lock ---
check "a narrow, decoupled ICardActivationBarrier exists, this project's own interface onto the shared host-layer lock" \
  test -f "$API_FEATURE/Domain/ICardActivationBarrier.cs"
check "every ProductionJobLockRegistry acquisition site also acquires the shared activation barrier, nested the same way - an invariant, not a fixed list" \
  bash -c "[ \"\$(grep -c 'lockRegistry.AcquireAsync' $API_FEATURE/ProductionService.cs)\" -eq \"\$(grep -c 'activationBarrier.AcquireAsync' $API_FEATURE/ProductionService.cs)\" ]"
check "the real adapter wraps the SAME shared Biolog.Api.Cards.CardActivationBarrierLockRegistry singleton BIO-CRD-006's own module also wraps" \
  bash -c "grep -q 'CardActivationBarrierAdapter' api/Biolog.Api/Modules/BioCrd007Module.cs && grep -q 'Biolog.Api.Cards.CardActivationBarrierLockRegistry' api/Biolog.Api/Modules/BioCrd007Module.cs"
check "a unit test double for the activation barrier exists, a real lock not a no-op stub" \
  bash -c "grep -q 'SemaphoreSlim' $API_TESTS/FakeCardActivationBarrier.cs"
check "a real-packaged-host test proves BIO-CRD-007 genuinely blocks on the SAME shared lock BIO-CRD-006 also uses, not an independent one of its own" \
  bash -c "grep -q 'Holding_the_shared_barrier_blocks_a_real_CRD007_create_job_call_until_released' $HOST_TESTS/CardActivationBarrierHostChannelTests.cs"

check "builder evidence recorded" test -d evidence/BIO-CRD-007/builder

echo "=============================="
echo "$pass passed, $fail failed"
[ "$fail" -eq 0 ]
