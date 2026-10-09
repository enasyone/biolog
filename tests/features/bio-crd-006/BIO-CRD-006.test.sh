#!/usr/bin/env bash
# Offline-safe structural checks for BIO-CRD-006 (report lost/missing/stolen/damaged and card
# replacement). Behavioural assertions live in the executable suites: the backend's own xunit
# tests (tests/features/bio-crd-006/) and the real-packaged-host tests
# (tests/dotnet/Biolog.IntegrationTests/CardLifecycleHostChannelTests.cs) - see
# docs/features/bio-crd-006/README.md. This story has no frontend UI - no AC asks for one,
# matching BIO-CRD-005's own backend-only precedent.
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"
pass=0; fail=0
check() { local label="$1"; shift; if "$@"; then echo "PASS  $label"; pass=$((pass+1)); else echo "FAIL  $label"; fail=$((fail+1)); fi; }

API_FEATURE=api/features/bio-crd-006
API_TESTS=tests/features/bio-crd-006
HOST_TESTS=tests/dotnet/Biolog.IntegrationTests

# --- AC01: reported active card immediately becomes locked/denied with configurable reason ---
check "AC01 a report is refused a second time while already Locked, not silently re-recorded" \
  bash -c "grep -q 'CardLifecycleError.AlreadyReported' $API_FEATURE/CardLifecycleService.cs"
check "AC01 reason_code is a plain, non-blank, tenant-configurable string, not a fixed enum" \
  bash -c "grep -q 'configurable reason' $API_FEATURE/Domain/CardLifecycleRecord.cs"
check "AC01 lock state is this story's OWN layer, deliberately not BIO-CRD-005's own one-way CardIssueStatus.Deactivated" \
  bash -c "grep -qi 'ONE-WAY' $API_FEATURE/Domain/CardLockState.cs"
check "a test proves reporting locks a card and records the configurable reason" \
  bash -c "grep -q 'Report_locks_a_card_and_records_the_configurable_reason' $API_TESTS/CardLifecycleServiceTests.cs"
check "a test proves presentation of a Locked card is denied immediately and logged" \
  bash -c "grep -q 'CheckPresentation_denies_and_logs_a_Locked_card_immediately_AC01' $API_TESTS/PresentationEventTests.cs"

# --- AC02: before replacement activation, permitted recovery may reactivate according to policy ---
check "AC02 a real, tenant-scoped, per-reason-code recovery policy store exists (not a hardcoded always-true)" \
  test -f "$API_FEATURE/Domain/ICardRecoveryPolicyStore.cs"
check "AC02 RecoverAsync consults the policy store before reactivating" \
  bash -c "grep -q 'policyStore.IsRecoveryPermitted' $API_FEATURE/CardLifecycleService.cs"
check "a test proves recovery is refused by tenant policy for a given reason code" \
  bash -c "grep -q 'Recover_honours_tenant_policy_that_refuses_recovery_for_a_reason_code' $API_TESTS/CardLifecycleServiceTests.cs"
check "a test proves recover transitions Locked to Recovered and presentation is allowed again" \
  bash -c "grep -q 'Recover_transitions_Locked_to_Recovered_and_presentation_is_allowed_again' $API_TESTS/CardLifecycleServiceTests.cs"
check "a test proves recovery after a later replacement's permanent revocation is refused (the AC02 'before activation' boundary)" \
  bash -c "grep -q 'After_permanent_revocation_recover_is_refused_even_though_it_was_once_Locked' $API_TESTS/CardLifecycleReplacementTests.cs"

# --- AC03: replacement uses next sequence ---
check "AC03 ReplaceAsync is a passthrough to BIO-CRD-005's own IssueAsync via the gateway - no separate sequence logic of its own" \
  bash -c "grep -q 'cardIssueGateway.IssueReplacementAsync' $API_FEATURE/CardLifecycleService.cs"
check "a test proves replacement uses the next sequence number" \
  bash -c "grep -q 'Replace_uses_the_next_sequence_number' $API_TESTS/CardLifecycleReplacementTests.cs"

# --- AC04: after replacement activation every earlier physical card is permanently non-reactivatable ---
check "AC04 a terminal PermanentlyRevoked lock state exists, distinct from the freely-reversible Locked/Recovered pair" \
  bash -c "grep -q 'PermanentlyRevoked' $API_FEATURE/Domain/CardLockState.cs"
check "AC04 ActivateReplacementAsync sweeps every earlier sequence number, not only the currently active one" \
  bash -c "grep -q 'foreach (var earlier in earlierCards)' $API_FEATURE/CardLifecycleService.cs"
check "AC04 an already PermanentlyRevoked record is skipped, making the sweep idempotent under a retried Idempotency-Key" \
  bash -c "grep -q 'already fully confirmed by an earlier attempt' $API_FEATURE/CardLifecycleService.cs"
check "AC04 activate-replacement requires Idempotency-Key (ADR-0004 §4.3 names card activation explicitly)" \
  bash -c "grep -q 'IdempotencyKeyRequired' $API_FEATURE/CardLifecycleService.cs"
check "a test proves every earlier card is permanently revoked, not only the one that was Active" \
  bash -c "grep -q 'ActivateReplacement_permanently_revokes_every_earlier_card_not_only_the_active_one' $API_TESTS/CardLifecycleReplacementTests.cs"
check "a test proves two concurrent activation attempts for the same replacement never both succeed" \
  bash -c "grep -q 'Concurrent_activation_attempts_for_the_same_replacement_never_both_succeed' $API_TESTS/CardLifecycleReplacementTests.cs"
check "a test proves replaying the same activate-replacement idempotency key does not double-revoke" \
  bash -c "grep -q 'Replaying_the_same_activate_replacement_idempotency_key_returns_the_original_result_without_double_revoking' $API_TESTS/CardLifecycleReplacementTests.cs"
check "BIO-CRD-006-QA-01 (Chioma's acceptance review round 1): every earlier card begins revocation strictly before the replacement is activated, not after" bash -c \
  "sweep_line=\$(grep -n 'await BeginRevocationAsync(tenantId, earlier,' $API_FEATURE/CardLifecycleService.cs | head -1 | cut -d: -f1);
   activate_line=\$(grep -n 'activated = await cardIssueGateway.ActivateAsync' $API_FEATURE/CardLifecycleService.cs | head -1 | cut -d: -f1);
   [ -n \"\$sweep_line\" ] && [ -n \"\$activate_line\" ] && [ \"\$sweep_line\" -lt \"\$activate_line\" ]"
check "BIO-CRD-006-QA-01 regression: a confirmed audit failure partway through the sweep leaves the replacement never activated" \
  bash -c "grep -q 'BIOCRD006QA01_a_confirmed_audit_failure_partway_through_the_sweep_leaves_the_replacement_never_activated' $API_TESTS/CardLifecycleActivationAtomicityTests.cs"
check "BIO-CRD-006-QA-02 (Chioma's acceptance review round 2): the replacement's own precondition is validated strictly before the earlier-card sweep, not after" bash -c \
  "validate_line=\$(grep -n 'RequireExpectedVersion(expectedVersion, target.Version);' $API_FEATURE/CardLifecycleService.cs | head -1 | cut -d: -f1);
   sweep_line=\$(grep -n 'await BeginRevocationAsync(tenantId, earlier,' $API_FEATURE/CardLifecycleService.cs | head -1 | cut -d: -f1);
   [ -n \"\$validate_line\" ] && [ -n \"\$sweep_line\" ] && [ \"\$validate_line\" -lt \"\$sweep_line\" ]"
check "BIO-CRD-006-QA-02: activate-replacement reserves its own Idempotency-Key at this story's own layer, independent of BIO-CRD-005's own claim" \
  bash -c "grep -q 'idempotencyStore.Claim(tenantId, ActivateReplacementOperation,' $API_FEATURE/CardLifecycleService.cs"
check "BIO-CRD-006-QA-02 regression: a stale expected_version on the replacement never touches any earlier card" \
  bash -c "grep -q 'BIOCRD006QA02_a_stale_expected_version_on_the_replacement_is_rejected_before_any_earlier_card_is_touched' $API_TESTS/CardLifecycleActivationAtomicityTests.cs"
check "BIO-CRD-006-QA-02 regression: reusing the activate-replacement idempotency key with different content never touches the second, distinct replacement" \
  bash -c "grep -q 'BIOCRD006QA02_reusing_an_activate_replacement_idempotency_key_with_different_content_is_rejected_before_any_earlier_card_is_touched' $API_TESTS/CardLifecycleActivationAtomicityTests.cs"
check "BIO-CRD-006-QA-03 (Chioma's acceptance review round 3): a non-terminal RevocationPending lock state exists so the sweep can be compensated, not only committed" \
  bash -c "grep -q 'RevocationPending' $API_FEATURE/Domain/CardLockState.cs"
check "BIO-CRD-006-QA-03: the prior lock state is durably recorded on the record itself, so a later, separate attempt can still compensate correctly" \
  bash -c "grep -q 'PriorLockStateBeforeRevocationPending' $API_FEATURE/Domain/CardLifecycleRecord.cs"
check "BIO-CRD-006-QA-03: a failed authoritative activation triggers compensation of every pending record, not a silent leave-as-is" \
  bash -c "grep -q 'CompensateRevocationAsync(tenantId, pendingRecord,' $API_FEATURE/CardLifecycleService.cs"
check "BIO-CRD-006-QA-03: RevocationPending denies presentation conservatively, never Allowed" \
  bash -c "grep -q 'CardLockState.RevocationPending => (PresentationOutcome.Denied' $API_FEATURE/CardLifecycleService.cs"
check "BIO-CRD-006-QA-03 regression: a concurrent mutation that fails the authoritative activation compensates a previously-Locked earlier card back to Locked" \
  bash -c "grep -q 'BIOCRD006QA03_a_concurrent_mutation_that_fails_the_authoritative_activation_compensates_a_previously_Locked_earlier_card_back_to_Locked' $API_TESTS/CardLifecycleActivationAtomicityTests.cs"
check "BIO-CRD-006-QA-03 regression: a compensated never-reported earlier card resolves to Unlocked, not PermanentlyRevoked or left dangling" \
  bash -c "grep -q 'BIOCRD006QA03_a_concurrent_mutation_that_fails_the_authoritative_activation_compensates_a_never_reported_earlier_card_back_to_Unlocked_and_presentation_is_allowed' $API_TESTS/CardLifecycleActivationAtomicityTests.cs"

# --- AC05: old-card presentation denies and logs, even when offline once revocation is available locally ---
check "AC05 a durable, append-only presentation-event log exists, separate from the ordinary audit trail" \
  test -f "$API_FEATURE/Domain/PresentationEvent.cs"
check "AC05 offline sync intake requires Idempotency-Key (ADR-0004 §4.3 names it explicitly)" \
  bash -c "grep -q 'IdempotencyKeyRequired' $API_FEATURE/CardLifecycleService.cs"
check "AC05 the deny-list feed includes Locked, RevocationPending and PermanentlyRevoked records" \
  bash -c "grep -q 'CardLockState.Locked or CardLockState.RevocationPending or CardLockState.PermanentlyRevoked' $API_FEATURE/Domain/ICardLifecycleRepository.cs"
check "AC05 offline intake never re-validates against this server's live state - the reader's own locally-cached decision is trusted" \
  bash -c "grep -q 'does NOT re-derive or override' $API_FEATURE/CardLifecycleService.cs"
check "a test proves an offline reader's decision is durably logged via the idempotent intake path" \
  bash -c "grep -q 'IntakePresentationEvent_durably_logs_a_decision_an_offline_reader_already_made_locally' $API_TESTS/PresentationEventTests.cs"
check "a test proves the deny list includes a Locked card so an offline reader can cache it before going offline" \
  bash -c "grep -q 'DenyList_includes_a_Locked_card_so_an_offline_reader_can_cache_it_before_going_offline' $API_TESTS/PresentationEventTests.cs"

# --- ADR-0001 identifier-registry discipline ---
check "no new canonical identifier type minted - a lifecycle record reuses BIO-CRD-005's own natural composite key" \
  bash -c "! grep -qE 'crd_[a-z]|CardLifecyclePrefix' $API_FEATURE/Domain/CardLifecycleRecord.cs"

# --- TD-CRD-006-DENY-LIST-DEVICE-AUTH: deny-list is device-authenticated, tenant-bound ---
check "deny-list endpoint requires a device authenticator before serving any data" \
  bash -c "grep -q 'IDeviceAuthenticator deviceAuthenticator' $API_FEATURE/Api/CardLifecycleEndpoints.cs && grep -q 'deviceAuthenticator.TryAuthenticateAsync' $API_FEATURE/Api/CardLifecycleEndpoints.cs"
check "a failed device authentication produces the generic device_authentication_failed error, not a distinguishable one" \
  bash -c "grep -q 'device_authentication_failed' $API_FEATURE/Domain/CardLifecycleError.cs"
check "device secrets are hashed at rest and compared in constant time, never stored/compared in plaintext" \
  bash -c "grep -q 'SHA256.HashData' $API_FEATURE/Domain/InMemoryDeviceCredentialStore.cs && grep -q 'FixedTimeEquals' $API_FEATURE/Domain/InMemoryDeviceCredentialStore.cs"
check "an unregistered device compares against a dummy hash, not a short-circuit - no timing oracle for device existence" \
  bash -c "grep -q 'DummyHash' $API_FEATURE/Domain/InMemoryDeviceCredentialStore.cs"
check "the device must also be existence/status checked, not secret-only - a valid secret for a retired device does not authenticate" \
  bash -c "grep -q 'DeviceStatus.Active' $API_FEATURE/Domain/IDeviceExistenceGateway.cs || grep -q 'DeviceStatus.Active' api/Biolog.Api/Modules/BioCrd006Module.cs"
check "device credentials are provisioned out of band via configuration, never through this feature's own HTTP surface" \
  test -f "$API_FEATURE/Domain/DeviceCredentialProvisioning.cs"
check "this feature's device-credential configuration section is its own, independent of BIO-PHO-002's" \
  bash -c "grep -q 'CardLifecycle:DeviceCredentials' $API_FEATURE/Domain/DeviceCredentialProvisioning.cs"
check "module wiring depends on DeviceRegistry (BIO-DEV-001) in addition to Tenant and CardIssuance" \
  bash -c "grep -q '\"Tenant\", \"CardIssuance\", \"CardProduction\", \"DeviceRegistry\"' api/Biolog.Api/Modules/BioCrd006Module.cs"
check "the device-existence adapter reuses DeviceService's own ambient-tenant lookup, so tenant binding is preserved for free, not reimplemented" \
  bash -c "grep -q 'DeviceExistenceGatewayAdapter' api/Biolog.Api/Modules/BioCrd006Module.cs && grep -q 'deviceService.GetAsync' api/Biolog.Api/Modules/BioCrd006Module.cs"
check "a device authenticator unit test file exists" \
  test -f "$API_TESTS/DeviceAuthenticatorTests.cs"
check "a unit test proves a device's real secret does not authenticate it against a different ambient tenant" \
  bash -c "grep -q 'A_devices_real_secret_does_not_authenticate_it_against_a_different_ambient_tenant' $API_TESTS/DeviceAuthenticatorTests.cs"
check "a test proves the deny-list endpoint itself rejects a request with no device headers at all" \
  bash -c "grep -q 'TDCRD006_DenyList_with_no_device_headers_at_all_is_401' $API_TESTS/CardLifecycleEndpointsIntegrationTests.cs"
check "a test proves a real device genuinely registered to a different tenant cannot authenticate this tenant's deny-list" \
  bash -c "grep -q 'TDCRD006_DenyList_a_devices_real_secret_does_not_authenticate_it_into_a_different_tenants_deny_list' $API_TESTS/CardLifecycleEndpointsIntegrationTests.cs"
check "a real-packaged-host test proves the fix end to end: no auth is 401 through the shipped module wiring" \
  bash -c "grep -q 'TDCRD006_deny_list_with_no_device_credentials_at_all_is_401_through_the_real_host' $HOST_TESTS/CardLifecycleHostChannelTests.cs"
check "a real-packaged-host test proves tenant binding with a genuinely real, cross-tenant-registered device" \
  bash -c "grep -q 'TDCRD006_a_device_genuinely_registered_in_tenant_A_cannot_authenticate_tenant_Bs_deny_list' $HOST_TESTS/CardLifecycleHostChannelTests.cs"

# --- Module wiring and write-scope discipline ---
check "no ProjectReference to any other feature project, including bio-crd-005 (a genuine runtime dependency reached only through this project's own gateway interface)" \
  bash -c "! grep -q '<ProjectReference' $API_FEATURE/Biolog.Features.CardLifecycle.csproj"
check "ICardIssueGateway is this project's own interface, not a reference to bio-crd-005's types" \
  test -f "$API_FEATURE/Domain/ICardIssueGateway.cs"
check "backend module lives one-way in api/Biolog.Api/Modules, not back in the feature project" \
  bash -c "test -f api/Biolog.Api/Modules/BioCrd006Module.cs && ! test -f '$API_FEATURE/BioCrd006Module.cs'"
check "registered in the packaged Program.cs module array, depending on Tenant and CardIssuance" \
  bash -c "grep -q 'new BioCrd006Module()' api/Biolog.Api/Program.cs && grep -q '\"Tenant\", \"CardIssuance\"' api/Biolog.Api/Modules/BioCrd006Module.cs"
check "the module adapter translates BIO-CRD-005's own CardIssueError into this feature's own CardLifecycleError, never leaking a sibling's exception type" \
  bash -c "grep -q 'catch (Biolog.Features.CardIssuance.Domain.CardIssueError error)' api/Biolog.Api/Modules/BioCrd006Module.cs"

# --- Developer test wiring ---
check "backend unit/integration tests exist" bash -c \
  'test -f tests/features/bio-crd-006/CardLifecycleRepositoryTests.cs &&
   test -f tests/features/bio-crd-006/CardLifecycleServiceTests.cs &&
   test -f tests/features/bio-crd-006/CardLifecycleReplacementTests.cs &&
   test -f tests/features/bio-crd-006/PresentationEventTests.cs &&
   test -f tests/features/bio-crd-006/CardLifecycleActivationAtomicityTests.cs &&
   test -f tests/features/bio-crd-006/CardLifecycleEndpointsIntegrationTests.cs'
check "a real-packaged-host test exists proving the genuine BIO-CRD-005 dependency through the shipped host channel" \
  test -f "$HOST_TESTS/CardLifecycleHostChannelTests.cs"
check "the host-channel test proves the real BIO-CRD-005 card issue is genuinely deactivated during the sweep, not only this story's own lock record" \
  bash -c "grep -q 'AC03_AC04_replacing_and_activating_the_replacement_through_the_real_host_permanently_revokes_the_real_earlier_card' $HOST_TESTS/CardLifecycleHostChannelTests.cs"
# --- TD-CRD-006-IDEMPOTENCY-RACE (High, Kemi-confirmed 2026-10-06) ---
check "the idempotency claim carries an explicit IsNewClaim ownership signal, not content-only State" \
  bash -c "grep -q 'IsNewClaim' $API_FEATURE/Domain/IdempotencyStore.cs"
check "both idempotency-guarded call sites branch on IsNewClaim, not on State alone" \
  bash -c "grep -c 'claim.IsNewClaim' $API_FEATURE/CardLifecycleService.cs | grep -qx 2"
check "a deterministic two-caller concurrency test exists proving exactly one execution per call site" \
  test -f tests/features/bio-crd-006/TdIdempotencyRaceTests.cs
check "the regression test forces genuine claim-boundary contention (a barrier-gated store), not just a shared start gate" \
  bash -c "grep -q 'ClaimBarrierIdempotencyStore' tests/features/bio-crd-006/TdIdempotencyRaceTests.cs"
check "a mutant-discrimination test proves the historical State-only check violates the same invariant the real fix satisfies" \
  bash -c "grep -q 'Mutant_discrimination' tests/features/bio-crd-006/TdIdempotencyRaceTests.cs"

# --- BIO-OPS-003-AC04 (Seyi's acceptance run, Kemi's architectural ruling 2026-10-07) ---
check "a narrow, read-only IProductionStatusGateway exists, this project's own interface onto BIO-CRD-007" \
  test -f "$API_FEATURE/Domain/IProductionStatusGateway.cs"
check "the gateway carries a decoupled enum mirror of BIO-CRD-007's own ProductionJobStatus, the same pattern CardIssueLifecycleStatus already uses" \
  bash -c "grep -q 'enum ProductionJobLifecycleStatus' $API_FEATURE/Domain/IProductionStatusGateway.cs"
check "ActivateReplacementAsync consults the production-status gateway, throwing ProductionNotPassed, before the revocation sweep" \
  bash -c "awk '/productionStatusGateway.HasPassedAsync/{p=NR} /earlierCards = siblingsBefore/{s=NR} END{exit !(p>0 && s>0 && p<s)}' $API_FEATURE/CardLifecycleService.cs"
check "ProductionNotPassed is a real error factory on this feature's own CardLifecycleError, not BIO-CRD-007's" \
  bash -c "grep -q 'CardLifecycleError ProductionNotPassed' $API_FEATURE/Domain/CardLifecycleError.cs"
check "the real adapter wraps BIO-CRD-007's own already-registered ProductionService.ListJobsAsync directly, the same shape as CardIssueGatewayAdapter" \
  bash -c "grep -q 'ProductionStatusGatewayAdapter' api/Biolog.Api/Modules/BioCrd006Module.cs && grep -q 'productionService.ListJobsAsync' api/Biolog.Api/Modules/BioCrd006Module.cs"
check "module wiring declares the new BIO-CRD-007 dependency, registered as CardProduction" \
  bash -c "grep -q '\"Tenant\", \"CardIssuance\", \"CardProduction\", \"DeviceRegistry\"' api/Biolog.Api/Modules/BioCrd006Module.cs"
check "a unit test proves activation is refused when production has not passed, and the earlier card is never swept" \
  bash -c "grep -q 'BIOOPS003AC04_activation_is_refused_when_production_has_not_passed_and_the_earlier_card_is_never_swept' $API_TESTS/CardLifecycleActivationAtomicityTests.cs"
check "a unit test proves activation still succeeds and sweeps normally once production has passed" \
  bash -c "grep -q 'BIOOPS003AC04_activation_still_succeeds_and_sweeps_normally_once_production_has_passed' $API_TESTS/CardLifecycleActivationAtomicityTests.cs"
check "a real-packaged-host test proves the composition gap is closed end to end: no real production job refuses activation and never touches the earlier real card" \
  bash -c "grep -q 'AC04_activating_a_replacement_through_the_real_host_is_refused_when_no_real_production_job_has_passed_and_the_earlier_card_stays_active' $HOST_TESTS/CardLifecycleHostChannelTests.cs"
check "the existing real-packaged-host replace+activate test now passes a real BIO-CRD-007 production job first" \
  bash -c "grep -q 'PassRealProductionJobAsync' $HOST_TESTS/CardLifecycleHostChannelTests.cs"

# --- BIO-OPS-003-AC04 round 2 (SY-OPS003-02, Kemi's ruling 2026-10-07): shared cross-feature lock ---
check "a narrow, decoupled ICardActivationBarrier exists, this project's own interface onto the shared host-layer lock" \
  test -f "$API_FEATURE/Domain/ICardActivationBarrier.cs"
check "ActivateReplacementAsync acquires the shared activation barrier right after its own lock, before the production-status check" \
  bash -c "awk '/lockRegistry.AcquireAsync\(tenantId, personId, cardClass, cancellationToken\)/{l=NR} /activationBarrier.AcquireAsync/{b=NR} /productionStatusGateway.HasPassedAsync/{p=NR} END{exit !(l>0 && b>0 && p>0 && l<b && b<p)}' $API_FEATURE/CardLifecycleService.cs"
check "the real adapter wraps the SAME shared Biolog.Api.Cards.CardActivationBarrierLockRegistry singleton BIO-CRD-007's own module also wraps" \
  bash -c "grep -q 'CardActivationBarrierAdapter' api/Biolog.Api/Modules/BioCrd006Module.cs && grep -q 'Biolog.Api.Cards.CardActivationBarrierLockRegistry' api/Biolog.Api/Modules/BioCrd006Module.cs"
check "a unit test double for the activation barrier exists, a real lock not a no-op stub" \
  bash -c "grep -q 'SemaphoreSlim' $API_TESTS/FakeCardActivationBarrier.cs"
check "a real-packaged-host test proves BIO-CRD-006 genuinely blocks on the SAME shared lock BIO-CRD-007 also uses, not an independent one of its own" \
  bash -c "grep -q 'Holding_the_shared_barrier_blocks_a_real_CRD006_activate_replacement_call_until_released' $HOST_TESTS/CardActivationBarrierHostChannelTests.cs"

check "builder evidence recorded" test -d evidence/BIO-CRD-006/builder

echo "=============================="
echo "$pass passed, $fail failed"
[ "$fail" -eq 0 ]
