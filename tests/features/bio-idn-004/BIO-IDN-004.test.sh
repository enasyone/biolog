#!/usr/bin/env bash
# Offline-safe structural checks for BIO-IDN-004 (identity lifecycle and emergency suspension).
# Behavioural assertions (explicit transitions, revocation events, permission/reason-gated
# reactivation, archived traceability, audit, HTTP pipeline) live in the executable xunit suites
# under tests/features/bio-idn-004/ and
# tests/dotnet/Biolog.IntegrationTests/PersonLifecycleHostChannelTests.cs (see
# docs/features/bio-idn-004/README.md).
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"
pass=0; fail=0
check() { local label="$1"; shift; if "$@"; then echo "PASS  $label"; pass=$((pass+1)); else echo "FAIL  $label"; fail=$((fail+1)); fi; }

FEATURE=api/features/bio-idn-004

# --- AC01: allowed identity transitions are explicit ---
check "AC01 feature project exists" test -f "$FEATURE/Biolog.Features.PersonLifecycle.csproj"
check "AC01 project targets the pinned .NET SDK's TFM" bash -c "grep -q '<TargetFramework>net10.0</TargetFramework>' '$FEATURE/Biolog.Features.PersonLifecycle.csproj'"
check "AC01 a closed, explicit transition whitelist exists, not a passthrough of any status" \
  bash -c "test -f '$FEATURE/Domain/LifecycleTransitionPolicy.cs' && grep -q 'IsAllowed' '$FEATURE/Domain/LifecycleTransitionPolicy.cs'"
check "AC01 ARCHIVED is terminal - no target status is ever listed for it" \
  bash -c "grep -q '\[PersonStatus.Archived\] = \[\]' '$FEATURE/Domain/LifecycleTransitionPolicy.cs'"
check "AC01 the service rejects an unlisted transition BEFORE calling the gateway" \
  bash -c "policy_line=\$(grep -n 'LifecycleTransitionPolicy.IsAllowed' '$FEATURE/PersonLifecycleService.cs' | head -1 | cut -d: -f1); gateway_line=\$(grep -n 'gateway.ChangeStatusAsync' '$FEATURE/PersonLifecycleService.cs' | head -1 | cut -d: -f1); [ -n \"\$policy_line\" ] && [ -n \"\$gateway_line\" ] && [ \"\$policy_line\" -lt \"\$gateway_line\" ]"
check "AC01 module lives one-way in api/Biolog.Api/Modules, not back in the feature project" \
  bash -c "test -f api/Biolog.Api/Modules/BioIdn004Module.cs && ! test -f '$FEATURE/BioIdn004Module.cs'"
check "AC01 registered in the packaged Program.cs module array" \
  bash -c "grep -q 'new BioIdn004Module()' api/Biolog.Api/Program.cs"
check "AC01 endpoints are versioned per ADR-0004 §1.1, nested under the owning person" \
  bash -c "grep -q '/api/v1/persons/{personId}/lifecycle' '$FEATURE/Api/PersonLifecycleEndpoints.cs'"
check "AC01 an exhaustive theory-driven test proves every allowed pair" \
  bash -c "grep -q 'AC01_every_allowed_pair_in_the_explicit_whitelist_is_allowed' tests/features/bio-idn-004/PersonLifecycleServiceTests.cs"
check "AC01 a theory-driven test proves representative disallowed pairs are rejected" \
  bash -c "grep -q 'AC01_every_other_pair_is_rejected' tests/features/bio-idn-004/PersonLifecycleServiceTests.cs"
check "AC01 no ProjectReference to BIO-IDN-001's project (the write seam is a narrow gateway, not a runtime coupling)" \
  bash -c "! grep -q '<ProjectReference.*bio-idn-001' '$FEATURE/Biolog.Features.PersonLifecycle.csproj'"

# --- AC02: emergency suspension emits credential/access revocation events ---
check "AC02 a dedicated CredentialAccessRevocationEvent type and sink exist, separate from the ordinary audit trail" \
  bash -c "test -f '$FEATURE/Domain/CredentialAccessRevocationEvent.cs' && test -f '$FEATURE/Domain/ICredentialAccessRevocationEventSink.cs'"
check "AC02 IsEmergencySuspension names exactly ACTIVE -> SUSPENDED, the only pair that qualifies" \
  bash -c "grep -q 'IsEmergencySuspension' '$FEATURE/Domain/LifecycleTransitionPolicy.cs' && grep -A2 'IsEmergencySuspension' '$FEATURE/Domain/LifecycleTransitionPolicy.cs' | grep -q 'PersonStatus.Active.*PersonStatus.Suspended'"
check "AC02 a suspension publishes to the revocation sink" \
  bash -c "grep -q 'revocationSink.PublishAsync\|PublishRevocationEventAsync' '$FEATURE/PersonLifecycleService.cs'"
check "AC02 a test proves the revocation event is published on suspension" \
  bash -c "grep -q 'AC02_emergency_suspension_publishes_a_credential_access_revocation_event' tests/features/bio-idn-004/PersonLifecycleServiceTests.cs"
check "AC02 a test proves a non-suspension transition never publishes one" \
  bash -c "grep -q 'AC02_a_non_suspension_transition_never_publishes_a_revocation_event' tests/features/bio-idn-004/PersonLifecycleServiceTests.cs"
check "AC02 suspension does not require a reason (distinguishing it from AC03's reactivation requirement)" \
  bash -c "grep -q 'AC02_emergency_suspension_does_not_require_a_reason' tests/features/bio-idn-004/PersonLifecycleServiceTests.cs"
check "QA-01 (round 2 regression) the gateway commits BEFORE the revocation publish for every transition, suspension included - a rejected request must never emit one" \
  bash -c "commit_line=\$(grep -n 'gateway.ChangeStatusAsync' '$FEATURE/PersonLifecycleService.cs' | head -1 | cut -d: -f1); publish_line=\$(grep -n 'PublishRevocationEventAsync(tenantId, personId, updated.Version' '$FEATURE/PersonLifecycleService.cs' | head -1 | cut -d: -f1); [ -n \"\$commit_line\" ] && [ -n \"\$publish_line\" ] && [ \"\$commit_line\" -lt \"\$publish_line\" ]"
check "QA-01 a distinct revocation_pending_reconciliation error exists, meaning the suspension committed but reconciliation is needed" \
  bash -c "grep -q 'RevocationPendingReconciliation' '$FEATURE/Domain/PersonLifecycleError.cs' && grep -q 'RevocationPendingReconciliation()' '$FEATURE/PersonLifecycleService.cs'"
check "QA-01 the revocation event is keyed by SuspendedAtVersion, making reconciliation idempotent and re-askable" \
  bash -c "grep -q 'SuspendedAtVersion' '$FEATURE/Domain/CredentialAccessRevocationEvent.cs' && grep -q 'ExistsForSuspensionAsync' '$FEATURE/Domain/ICredentialAccessRevocationEventSink.cs'"
check "QA-01 (round 3) a pending reconciliation is tracked as its own durable record, never derived from the person's current status" \
  bash -c "test -f '$FEATURE/Domain/PendingSuspensionReconciliation.cs' && test -f '$FEATURE/Domain/IPendingSuspensionReconciliationStore.cs' && grep -q 'pendingStore.MarkPendingAsync' '$FEATURE/PersonLifecycleService.cs'"
check "QA-01 (round 3) a dedicated reconciliation operation is reachable regardless of the person's current status, not tied to any particular transition shape" \
  bash -c "grep -q 'ReconcilePendingRevocationsAsync' '$FEATURE/PersonLifecycleService.cs' && grep -q '/revocations/reconcile' '$FEATURE/Api/PersonLifecycleEndpoints.cs'"
check "QA-01 (round 3) the pending fact is queried directly from the durable store, not derived from status" \
  bash -c "grep -q 'HasPendingRevocationsAsync' '$FEATURE/PersonLifecycleService.cs' && grep -q 'pendingStore.ListPendingAsync' '$FEATURE/PersonLifecycleService.cs' && grep -q 'revocation_pending' '$FEATURE/Api/PersonLifecycleResponse.cs'"
check "QA-01 (round 2 regression) a break-it-on-purpose-verified test proves a rejected suspension never emits a false revocation event" \
  bash -c "grep -q 'BIOIDN004_QA01_round2_a_rejected_suspension_never_emits_a_false_revocation_event' tests/features/bio-idn-004/PersonLifecycleAuditTests.cs"
check "QA-01 (round 1 regression) a break-it-on-purpose-verified test proves a revocation failure still commits and enters pending reconciliation" \
  bash -c "grep -q 'BIOIDN004_QA01_round3_when_the_revocation_sink_fails_the_suspension_still_commits_and_enters_pending_reconciliation' tests/features/bio-idn-004/PersonLifecycleAuditTests.cs"
check "QA-01 a break-it-on-purpose-verified test proves the reconciliation operation closes the gap exactly once, even called repeatedly" \
  bash -c "grep -q 'BIOIDN004_QA01_a_reconciliation_retry_after_the_sink_recovers_completes_the_pending_revocation_exactly_once' tests/features/bio-idn-004/PersonLifecycleAuditTests.cs"
check "QA-01 (round 3, the finding itself) a break-it-on-purpose-verified test proves the pending fact survives a reactivation and stays observable/reconcilable" \
  bash -c "grep -q 'BIOIDN004_QA01_round4_the_pending_fact_survives_a_reactivation_and_stays_observable_and_reconcilable' tests/features/bio-idn-004/PersonLifecycleAuditTests.cs"
check "QA-01 (round 4) the pending record is marked BEFORE either write that can throw, not after the ordinary audit write" \
  bash -c "mark_line=\$(grep -n 'pendingStore.MarkPendingAsync' '$FEATURE/PersonLifecycleService.cs' | head -1 | cut -d: -f1); audit_line=\$(grep -n 'await WriteLifecycleEventAsync(tenantId' '$FEATURE/PersonLifecycleService.cs' | head -1 | cut -d: -f1); [ -n \"\$mark_line\" ] && [ -n \"\$audit_line\" ] && [ \"\$mark_line\" -lt \"\$audit_line\" ]"
check "QA-01 (round 4) the ordinary audit write and the revocation publish are attempted independently of one another's outcome" \
  bash -c "grep -q 'catch (PersonLifecycleError ex)' '$FEATURE/PersonLifecycleService.cs' && grep -q 'auditError = ex' '$FEATURE/PersonLifecycleService.cs'"
check "QA-01 (round 4, the finding itself) a break-it-on-purpose-verified test proves the pending record survives an audit-sink outage during a suspension" \
  bash -c "grep -q 'BIOIDN004_QA01_round5_an_audit_sink_outage_during_a_suspension_still_leaves_a_pending_record_behind' tests/features/bio-idn-004/PersonLifecycleAuditTests.cs"
check "QA-01 a test proves the audit write and the revocation publish are genuinely independent attempts" \
  bash -c "grep -q 'An_audit_sink_outage_during_a_suspension_still_attempts_the_revocation_publish' tests/features/bio-idn-004/PersonLifecycleAuditTests.cs"

# --- AC03: reactivation requires permission and audit reason ---
check "AC03 a permission-header seam exists, separate from actor identity" \
  bash -c "test -f '$FEATURE/Api/PermissionResolver.cs' && grep -q 'X-Actor-Permission' '$FEATURE/Api/PermissionResolver.cs'"
check "AC03 IsReactivation names exactly SUSPENDED -> ACTIVE, the only pair that qualifies" \
  bash -c "grep -q 'IsReactivation' '$FEATURE/Domain/LifecycleTransitionPolicy.cs' && grep -A2 'IsReactivation' '$FEATURE/Domain/LifecycleTransitionPolicy.cs' | grep -q 'PersonStatus.Suspended.*PersonStatus.Active'"
check "AC03 reactivation without the asserted permission is rejected" \
  bash -c "grep -q 'PermissionRequired' '$FEATURE/Domain/PersonLifecycleError.cs' && grep -q 'PermissionRequired(PermissionResolver.ReactivatePersonPermission)' '$FEATURE/PersonLifecycleService.cs'"
check "AC03 reactivation without a reason is rejected even with permission" \
  bash -c "grep -q 'ReasonRequired' '$FEATURE/Domain/PersonLifecycleError.cs' && grep -q 'ReasonRequired()' '$FEATURE/PersonLifecycleService.cs'"
check "AC03 tests prove both gates and that initial activation needs neither" \
  bash -c "grep -q 'AC03_reactivation_without_the_permission_header_is_rejected' tests/features/bio-idn-004/PersonLifecycleServiceTests.cs && grep -q 'AC03_reactivation_without_a_reason_is_rejected_even_with_permission' tests/features/bio-idn-004/PersonLifecycleServiceTests.cs && grep -q 'AC03_the_initial_activation_from_VERIFIED_needs_neither_permission_nor_reason' tests/features/bio-idn-004/PersonLifecycleServiceTests.cs"

# --- AC04: archived records remain traceable ---
check "AC04 a lifecycle-events read endpoint exists, unconditional on the person's current status" \
  bash -c "grep -q '/events' '$FEATURE/Api/PersonLifecycleEndpoints.cs' && grep -q 'ListByPersonAsync' '$FEATURE/Audit/IPersonLifecycleAuditSink.cs'"
check "AC04 a test proves the full event history survives archival" \
  bash -c "grep -q 'AC04_the_full_lifecycle_event_history_survives_archival' tests/features/bio-idn-004/PersonLifecycleServiceTests.cs"
check "AC04 a test proves the snapshot itself remains readable after archival" \
  bash -c "grep -q 'AC04_an_archived_person_s_snapshot_is_still_readable' tests/features/bio-idn-004/PersonLifecycleServiceTests.cs"
check "AC04 a test proves further transitions on an archived person fail while reads keep working" \
  bash -c "grep -q 'AC04_a_further_transition_attempt_on_an_archived_person_is_rejected_but_reads_still_work' tests/features/bio-idn-004/PersonLifecycleServiceTests.cs"

# --- The write-capable gateway (extends BIO-IDN-002/003's read-only IPersonExistenceGateway pattern) ---
check "a Result-shaped, not exception-throwing, write gateway interface exists" \
  bash -c "test -f '$FEATURE/Domain/IPersonLifecycleGateway.cs' && grep -q 'internal interface IPersonLifecycleGateway' '$FEATURE/Domain/IPersonLifecycleGateway.cs' && grep -q 'LifecycleChangeResult' '$FEATURE/Domain/IPersonLifecycleGateway.cs'"
check "the real adapter wraps BIO-IDN-001's own PersonService.GetAsync/UpdateStatusAsync (no reimplemented tenant or transition logic)" \
  bash -c "grep -q 'PersonLifecycleGatewayAdapter' api/Biolog.Api/Modules/BioIdn004Module.cs && grep -q 'personService.UpdateStatusAsync' api/Biolog.Api/Modules/BioIdn004Module.cs && grep -q 'personService.GetAsync' api/Biolog.Api/Modules/BioIdn004Module.cs"
check "the adapter catches BIO-IDN-001's own PersonError and translates its Code, never leaking the exception type" \
  bash -c "grep -q 'catch (Biolog.Features.PersonRegistry.Domain.PersonError error)' api/Biolog.Api/Modules/BioIdn004Module.cs"
check "real-packaged-host tests prove the write reaches BIO-IDN-001's real store and respects the tenant boundary" \
  bash -c "grep -q 'Emergency_suspension_through_a_verified_hostname_resolves_the_real_tenant_and_reaches_BIO_IDN_001_s_real_store' tests/dotnet/Biolog.IntegrationTests/PersonLifecycleHostChannelTests.cs && grep -q 'BIOIDN004_a_tenant_B_caller_cannot_suspend_a_real_tenant_A_person' tests/dotnet/Biolog.IntegrationTests/PersonLifecycleHostChannelTests.cs"

# --- Tenant boundary, mandatory ---
check "the service reads the resolved tenant, never a caller-supplied id" \
  bash -c "[ \"\$(grep -c 'RequireTenant()' '$FEATURE/PersonLifecycleService.cs')\" -ge 3 ]"
check "a missing tenant context fails closed (tenant_context_missing, 400)" \
  bash -c "grep -q 'TenantContextMissing' '$FEATURE/Domain/PersonLifecycleError.cs' && grep -A2 'TenantContextMissing()' '$FEATURE/Domain/PersonLifecycleError.cs' | grep -q ', 400)'"
check "a missing or foreign-tenant person is rejected as person_not_found (404), not a distinct forbidden" \
  bash -c "grep -q 'PersonNotFound' '$FEATURE/Domain/PersonLifecycleError.cs' && grep -A2 'PersonNotFound(string personId)' '$FEATURE/Domain/PersonLifecycleError.cs' | grep -q ', 404,'"
check "real shipped-host positive-path test exists (ADR-0002 Decision 6 obligation)" \
  bash -c "test -f tests/dotnet/Biolog.IntegrationTests/PersonLifecycleHostChannelTests.cs && grep -q 'WebApplicationFactory<Program>' tests/dotnet/Biolog.IntegrationTests/PersonLifecycleHostChannelTests.cs"

# --- ADR-0003 Decision 7: audit-atomicity, adapted for a cross-feature write boundary ---
check "a per-person lock exists, in its own singleton-safe class" \
  bash -c "test -f '$FEATURE/PersonLifecycleLockRegistry.cs' && grep -q 'AcquireAsync' '$FEATURE/PersonLifecycleLockRegistry.cs'"
check "the lock lives in its own class because PersonLifecycleService must be Scoped (captive-dependency avoidance)" \
  bash -c "grep -q 'AddScoped<PersonLifecycleService>' api/Biolog.Api/Modules/BioIdn004Module.cs && grep -q 'AddSingleton<PersonLifecycleLockRegistry>' api/Biolog.Api/Modules/BioIdn004Module.cs"
check "the gateway call (the real commit) happens BEFORE this feature's own audit write, the reverse order from every sibling story" \
  bash -c "lock_line=\$(grep -n 'gateway.ChangeStatusAsync' '$FEATURE/PersonLifecycleService.cs' | head -1 | cut -d: -f1); audit_line=\$(grep -n 'WriteLifecycleEventAsync(tenantId' '$FEATURE/PersonLifecycleService.cs' | head -1 | cut -d: -f1); [ -n \"\$lock_line\" ] && [ -n \"\$audit_line\" ] && [ \"\$lock_line\" -lt \"\$audit_line\" ]"
check "ExistsAsync verification on an ambiguous audit-write throw" \
  bash -c "grep -q 'ExistsAsync' '$FEATURE/Audit/IPersonLifecycleAuditSink.cs' && grep -q 'auditSink.ExistsAsync' '$FEATURE/PersonLifecycleService.cs'"
check "a same-person concurrency race is proven" \
  bash -c "grep -q 'Two_genuinely_concurrent_transitions_of_the_same_person_never_both_succeed' tests/features/bio-idn-004/PersonLifecycleAuditTests.cs"
check "a test proves the underlying change committed even when this feature's own audit write fails" \
  bash -c "grep -q 'When_this_feature_s_own_audit_sink_fails_the_underlying_status_change_has_already_committed' tests/features/bio-idn-004/PersonLifecycleAuditTests.cs"
check "a test proves a gateway failure records no audit event at all" \
  bash -c "grep -q 'A_gateway_failure_records_no_audit_event' tests/features/bio-idn-004/PersonLifecycleAuditTests.cs"

# --- Developer test wiring ---
check "unit/audit/scope tests reference the feature project" \
  bash -c "grep -q 'Biolog.Features.PersonLifecycle.csproj' tests/features/bio-idn-004/Biolog.Features.PersonLifecycle.Tests.csproj"
check "feature-local integration tests exist and reference TestHost" \
  bash -c "test -f tests/features/bio-idn-004/PersonLifecycleEndpointsIntegrationTests.cs && grep -q 'Microsoft.AspNetCore.TestHost' tests/features/bio-idn-004/Biolog.Features.PersonLifecycle.Tests.csproj"
check "docs/features/bio-idn-004/README.md documents the feature" test -f docs/features/bio-idn-004/README.md
check "builder evidence recorded" test -d evidence/features/bio-idn-004/builder

echo "=============================="
echo "$pass passed, $fail failed"
[ "$fail" -eq 0 ]
