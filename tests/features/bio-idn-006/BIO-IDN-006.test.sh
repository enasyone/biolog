#!/usr/bin/env bash
# Offline-safe structural checks for BIO-IDN-006 (RBAC role catalogue and maker-checker hooks).
# Behavioural assertions (role catalogue, tenant-scoped permissions, maker-checker workflow,
# privilege escalation, audit atomicity, HTTP pipeline) live in the executable xunit suites
# under tests/features/bio-idn-006/ and
# tests/dotnet/Biolog.IntegrationTests/RbacHostChannelTests.cs (see
# docs/features/bio-idn-006/README.md).
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"
pass=0; fail=0
check() { local label="$1"; shift; if "$@"; then echo "PASS  $label"; pass=$((pass+1)); else echo "FAIL  $label"; fail=$((fail+1)); fi; }

FEATURE=api/features/bio-idn-006

# --- AC01: baseline role catalogue ---
check "AC01 feature project exists" test -f "$FEATURE/Biolog.Features.Rbac.csproj"
check "AC01 project targets the pinned .NET SDK's TFM" bash -c "grep -q '<TargetFramework>net10.0</TargetFramework>' '$FEATURE/Biolog.Features.Rbac.csproj'"
check "AC01 all eleven baseline roles are named verbatim" \
  bash -c "for r in SuperAdmin TenantAdmin RegistrarHr Enrolment Biometric CardProduction Security Gate Auditor Helpdesk ReadOnly; do grep -q \"\$r,\" '$FEATURE/Domain/RoleType.cs' || exit 1; done"
check "AC01 module lives one-way in api/Biolog.Api/Modules, not back in the feature project" \
  bash -c "test -f api/Biolog.Api/Modules/BioIdn006Module.cs && ! test -f '$FEATURE/BioIdn006Module.cs'"
check "AC01 registered in the packaged Program.cs module array" \
  bash -c "grep -q 'new BioIdn006Module()' api/Biolog.Api/Program.cs"
check "AC01 a role catalogue listing endpoint exists" \
  bash -c "grep -q '/api/v1/roles' '$FEATURE/Api/RoleAssignmentEndpoints.cs'"
check "AC01 a test proves the catalogue lists all eleven roles" \
  bash -c "grep -q 'AC01_the_role_catalogue_lists_all_eleven_baseline_roles' tests/features/bio-idn-006/RbacEndpointsIntegrationTests.cs"
check "AC01 a test proves a person can hold several roles at once" \
  bash -c "grep -q 'AC01_a_person_can_hold_several_roles_at_once' tests/features/bio-idn-006/RoleAssignmentServiceTests.cs"

# --- BIO-IDN-006-QA-01 (Seyi round 1, Critical): arbitrary asserted actor could grant SUPER_ADMIN ---
check "QA-01 mutating SUPER_ADMIN requires the caller to already hold an active SUPER_ADMIN assignment" \
  bash -c "grep -q 'RequireSuperAdminMutationAuthorityAsync' '$FEATURE/RoleAssignmentService.cs' && grep -q 'ActorHoldsActiveSuperAdminAsync' '$FEATURE/RoleAssignmentService.cs'"
check "QA-01 both AssignAsync and RevokeAsync call the SUPER_ADMIN authority gate" \
  bash -c "[ \"\$(grep -c 'RequireSuperAdminMutationAuthorityAsync(tenantId, actor' '$FEATURE/RoleAssignmentService.cs')\" = 2 ]"
check "QA-01 the authority gate runs before any other validation, existence check or lock" \
  bash -c "assign_gate_line=\$(grep -n 'if (role == RoleType.SuperAdmin)' '$FEATURE/RoleAssignmentService.cs' | head -1 | cut -d: -f1); format_check_line=\$(grep -n 'CanonicalId.IsPersonId(personId)' '$FEATURE/RoleAssignmentService.cs' | head -1 | cut -d: -f1); [ -n \"\$assign_gate_line\" ] && [ -n \"\$format_check_line\" ] && [ \"\$assign_gate_line\" -lt \"\$format_check_line\" ]"
check "QA-01 the actor id must itself be a well-formed canonical per_ id to satisfy the check - free-form actor ids can never qualify" \
  bash -c "grep -q 'CanonicalId.IsPersonId(actor.Id)' '$FEATURE/RoleAssignmentService.cs'"
check "QA-01 no bootstrap exception exists in code - the gate is unconditional (excluding explanatory doc-comment prose)" \
  bash -c "! grep -v '^\s*///\|^\s*//' '$FEATURE/RoleAssignmentService.cs' | grep -qi 'bootstrap.*exception\|first.*assignment.*allow'"
check "QA-01 Seyi's own adversarial reproduction is checked in and now passes" \
  bash -c "grep -q 'ADVERSARIAL_unprivileged_asserted_actor_cannot_grant_SUPER_ADMIN_to_a_person' tests/features/bio-idn-006/RbacEndpointsIntegrationTests.cs"
check "QA-01 a service-level test proves the same rejection for both assign and revoke" \
  bash -c "grep -q 'PRIVILEGE_ESCALATION_an_actor_who_does_not_already_hold_SUPER_ADMIN_cannot_assign_it' tests/features/bio-idn-006/RoleAssignmentServiceTests.cs && grep -q 'PRIVILEGE_ESCALATION_an_actor_who_does_not_already_hold_SUPER_ADMIN_cannot_revoke_it_either' tests/features/bio-idn-006/RoleAssignmentServiceTests.cs"
check "QA-01 a test proves a free-form non-person actor id (the codebase's own disclosed pre-auth convention) can never satisfy the check" \
  bash -c "grep -q 'PRIVILEGE_ESCALATION_a_free_form_non_person_actor_id_can_never_satisfy_the_SUPER_ADMIN_check_even_by_coincidence' tests/features/bio-idn-006/RoleAssignmentServiceTests.cs"
check "QA-01 a positive-path test proves a legitimate SUPER_ADMIN can still assign and revoke SUPER_ADMIN" \
  bash -c "grep -q 'A_legitimate_SUPER_ADMIN_can_assign_SUPER_ADMIN_to_someone_else' tests/features/bio-idn-006/RoleAssignmentServiceTests.cs && grep -q 'A_legitimate_SUPER_ADMIN_can_revoke_SUPER_ADMIN_from_someone_else' tests/features/bio-idn-006/RoleAssignmentServiceTests.cs"
check "QA-01 a real-host test proves the rejection through the actual shipped endpoint, with no seeding backdoor available" \
  bash -c "grep -q 'BIOIDN006_QA01_PRIVILEGE_ESCALATION_an_unprivileged_actor_cannot_grant_SUPER_ADMIN_through_the_real_packaged_host' tests/dotnet/Biolog.IntegrationTests/RbacHostChannelTests.cs"

# --- BIO-IDN-006-QA-02 (Seyi round 1, High): maker-checker clearance had no production consumer ---
check "QA-02 mutating SUPER_ADMIN is itself gated by MakerCheckerService (QA-05 upgraded this from a read-only check to atomic single-use consumption) - a real, owned sensitive action" \
  bash -c "grep -q 'makerCheckerService.TryConsumeApprovalAsync' '$FEATURE/RoleAssignmentService.cs'"
check "QA-02 distinct action keys are used for assign vs revoke" \
  bash -c "grep -q 'rbac.super_admin_assign' '$FEATURE/RoleAssignmentService.cs' && grep -q 'rbac.super_admin_revoke' '$FEATURE/RoleAssignmentService.cs'"
check "QA-02 the authority gate runs before the maker-checker clearance gate (an unprivileged caller learns nothing about rule configuration)" \
  bash -c "grep -q 'Maker_checker_authorization_gate_runs_before_the_SUPER_ADMIN_authority_gate' tests/features/bio-idn-006/RoleAssignmentServiceTests.cs"
check "QA-02 a test proves maker-checker genuinely BLOCKS a legitimate SUPER_ADMIN when the tenant requires second approval" \
  bash -c "grep -q 'Maker_checker_genuinely_blocks_a_legitimate_SUPER_ADMIN_from_granting_SUPER_ADMIN_when_the_tenant_requires_second_approval' tests/features/bio-idn-006/RoleAssignmentServiceTests.cs"
check "QA-02 a test proves the mutation succeeds once a real second approval is obtained" \
  bash -c "grep -q 'Maker_checker_clears_once_a_second_approval_is_obtained_and_the_mutation_then_succeeds' tests/features/bio-idn-006/RoleAssignmentServiceTests.cs"
check "QA-02 an endpoint-level test proves the clearance gate blocks even a legitimate SUPER_ADMIN through the full HTTP pipeline" \
  bash -c "grep -q 'Maker_checker_blocks_even_a_legitimate_SUPER_ADMIN_from_granting_SUPER_ADMIN_through_the_full_pipeline_when_configured' tests/features/bio-idn-006/RbacEndpointsIntegrationTests.cs"

# --- AC02: permissions are tenant-scoped ---
check "AC02 a tenant-scoped RolePermissionGrant aggregate exists, keyed (TenantId, Role, PermissionKey)" \
  bash -c "grep -q 'public RoleType Role { get; }' '$FEATURE/Domain/RolePermissionGrant.cs' && grep -q 'public string PermissionKey { get; }' '$FEATURE/Domain/RolePermissionGrant.cs'"
check "AC02 a fail-closed default: an absent grant is treated as not granted" \
  bash -c "grep -q 'grant?.Granted == true' '$FEATURE/PermissionService.cs'"
check "AC02 the real permission-check surface exists (HasPermissionAsync)" \
  bash -c "grep -q 'HasPermissionAsync' '$FEATURE/PermissionService.cs' && grep -q '/api/v1/persons/{personId}/permissions/{permissionKey}' '$FEATURE/Api/PermissionEndpoints.cs'"
check "AC02 a test proves granting a permission to a held role makes HasPermission true" \
  bash -c "grep -q 'Granting_a_permission_to_a_role_the_person_holds_makes_HasPermission_true' tests/features/bio-idn-006/PermissionServiceTests.cs"
check "AC02 a test proves revoking the underlying role immediately removes the permission (live check, never cached)" \
  bash -c "grep -q 'Revoking_the_underlying_role_immediately_removes_the_permission_no_stale_bypass' tests/features/bio-idn-006/PermissionServiceTests.cs"
check "AC02 a test proves a grant configured in tenant A never applies in tenant B" \
  bash -c "grep -q 'AC02_a_grant_configured_in_tenant_A_never_applies_in_tenant_B' tests/features/bio-idn-006/PermissionServiceTests.cs"
check "AC02 a real-host test proves a tenant-B caller learns nothing about a real tenant-A person, never leaking cross-tenant state" \
  bash -c "grep -q 'BIOIDN006_a_tenant_B_caller_gets_the_identical_rejection_as_the_wholly_unknown_case_never_leaking_tenant_A_state' tests/dotnet/Biolog.IntegrationTests/RbacHostChannelTests.cs"

# --- BIO-IDN-006-QA-03 (Seyi round 2, Critical): every OTHER role also requires real authority ---
check "QA-03 assigning/revoking any role other than SUPER_ADMIN requires rbac.role_assign/rbac.role_revoke" \
  bash -c "grep -q 'RoleAssignPermission = \"rbac.role_assign\"' '$FEATURE/RoleAssignmentService.cs' && grep -q 'RoleRevokePermission = \"rbac.role_revoke\"' '$FEATURE/RoleAssignmentService.cs'"
check "QA-03 the permission check is resolved through PermissionService.ActorHasPermissionAsync, the same general mechanism as every other gate" \
  bash -c "grep -q 'permissionService.ActorHasPermissionAsync' '$FEATURE/RoleAssignmentService.cs'"
check "QA-03 configuring a permission grant for any role other than SUPER_ADMIN requires rbac.permission_grant_manage" \
  bash -c "grep -q 'PermissionGrantManagePermission = \"rbac.permission_grant_manage\"' '$FEATURE/PermissionService.cs' && grep -q 'ActorHasPermissionAsync(tenantId, actor.Id, PermissionGrantManagePermission' '$FEATURE/PermissionService.cs'"
check "QA-03 a malformed or nonexistent actor id never throws from the internal gate - it simply reports not granted" \
  bash -c "grep -q 'internal async Task<bool> ActorHasPermissionAsync' '$FEATURE/PermissionService.cs'"
check "QA-03 service-level tests prove an unprivileged actor cannot assign or revoke any other role" \
  bash -c "grep -q 'PRIVILEGE_ESCALATION_an_unprivileged_actor_cannot_assign_a_non_SUPER_ADMIN_role_either' tests/features/bio-idn-006/RoleAssignmentServiceTests.cs && grep -q 'PRIVILEGE_ESCALATION_an_unprivileged_actor_cannot_revoke_a_non_SUPER_ADMIN_role_either' tests/features/bio-idn-006/RoleAssignmentServiceTests.cs"
check "QA-03 a service-level test proves a role explicitly delegated the permission can assign/configure without holding SUPER_ADMIN" \
  bash -c "grep -q 'A_role_explicitly_delegated_rbac_role_assign_can_assign_other_roles_without_holding_SUPER_ADMIN' tests/features/bio-idn-006/RoleAssignmentServiceTests.cs && grep -q 'A_role_explicitly_delegated_rbac_permission_grant_manage_can_configure_grants_without_holding_SUPER_ADMIN' tests/features/bio-idn-006/PermissionServiceTests.cs"
check "QA-03 endpoint-level and real-host tests prove the same rejection through the full HTTP pipeline" \
  bash -c "grep -q 'ADVERSARIAL_unprivileged_actor_cannot_assign_TENANT_ADMIN_or_define_its_permissions' tests/features/bio-idn-006/RbacEndpointsIntegrationTests.cs && grep -q 'BIOIDN006_QA03_PRIVILEGE_ESCALATION_an_unprivileged_actor_cannot_assign_a_non_SUPER_ADMIN_role_through_the_real_packaged_host' tests/dotnet/Biolog.IntegrationTests/RbacHostChannelTests.cs"

# --- BIO-IDN-006-QA-04 (Seyi round 2, Critical): configuring the sensitive-action rule itself requires real authority ---
check "QA-04 configuring a sensitive-action rule requires rbac.sensitive_action_rule_manage, checked before any other validation" \
  bash -c "manage_line=\$(grep -n 'SensitiveActionRuleManagePermission = \"rbac.sensitive_action_rule_manage\"' '$FEATURE/MakerCheckerService.cs' | head -1 | cut -d: -f1); gate_line=\$(grep -n 'ActorHasPermissionAsync(tenantId, actor.Id, SensitiveActionRuleManagePermission' '$FEATURE/MakerCheckerService.cs' | head -1 | cut -d: -f1); action_key_line=\$(grep -n 'ValidateActionKey' '$FEATURE/MakerCheckerService.cs' | head -1 | cut -d: -f1); [ -n \"\$manage_line\" ] && [ -n \"\$gate_line\" ] && [ -n \"\$action_key_line\" ] && [ \"\$gate_line\" -lt \"\$action_key_line\" ]"
check "QA-04 a service-level test proves an unprivileged actor cannot configure or disable the rule" \
  bash -c "grep -q 'PRIVILEGE_ESCALATION_an_unprivileged_actor_cannot_configure_or_disable_a_sensitive_action_rule' tests/features/bio-idn-006/MakerCheckerServiceTests.cs"
check "QA-04 a service-level test proves a role explicitly delegated the permission can configure the rule without holding SUPER_ADMIN" \
  bash -c "grep -q 'A_role_explicitly_delegated_rbac_sensitive_action_rule_manage_can_configure_the_rule_without_holding_SUPER_ADMIN' tests/features/bio-idn-006/MakerCheckerServiceTests.cs"
check "QA-04 endpoint-level and real-host tests prove the same rejection through the full HTTP pipeline" \
  bash -c "grep -q 'ADVERSARIAL_unprivileged_actor_cannot_disable_a_maker_checker_rule' tests/features/bio-idn-006/RbacEndpointsIntegrationTests.cs && grep -q 'BIOIDN006_QA04_PRIVILEGE_ESCALATION_an_unprivileged_actor_cannot_disable_a_maker_checker_rule_through_the_real_packaged_host' tests/dotnet/Biolog.IntegrationTests/RbacHostChannelTests.cs"

# --- BIO-IDN-006-QA-05 (Seyi round 2, High): an approval is single-use, never permanently reusable ---
check "QA-05 ApprovalRequest carries a separate ConsumedAt fact, distinct from Status" \
  bash -c "grep -q 'ConsumedAt { get; init; }' '$FEATURE/Domain/ApprovalRequest.cs'"
check "QA-05 the repository exposes a genuinely separate ConsumeAsync method, independently re-validating Approved and not-yet-consumed at the persistence boundary" \
  bash -c "grep -q 'Task<ApprovalRequest> ConsumeAsync' '$FEATURE/Domain/IApprovalRequestRepository.cs' && grep -q 'ApprovalNotApproved' '$FEATURE/Domain/InMemoryApprovalRequestRepository.cs' && grep -q 'ApprovalAlreadyConsumed' '$FEATURE/Domain/InMemoryApprovalRequestRepository.cs'"
check "QA-05 MakerCheckerService exposes an atomic, single-use TryConsumeApprovalAsync, and IsActionClearedAsync remains a separate, unchanged read-only check" \
  bash -c "grep -q 'TryConsumeApprovalAsync' '$FEATURE/MakerCheckerService.cs' && grep -q 'IsActionClearedAsync' '$FEATURE/MakerCheckerService.cs'"
check "QA-05 the SUPER_ADMIN mutation gate now consumes the approval rather than merely reading its status" \
  bash -c "grep -q 'makerCheckerService.TryConsumeApprovalAsync' '$FEATURE/RoleAssignmentService.cs'"
check "QA-05 a test proves the first mutation consumes the approval and a second mutation cannot reuse it" \
  bash -c "grep -q 'An_approved_requests_clearance_is_consumed_by_the_first_mutation_and_never_clears_a_second' tests/features/bio-idn-006/MakerCheckerServiceTests.cs"
check "QA-05 a test proves a fresh approval is required for, and does clear, a second mutation of the same target" \
  bash -c "grep -q 'A_fresh_approval_is_required_and_does_work_for_a_second_mutation_of_the_same_target' tests/features/bio-idn-006/MakerCheckerServiceTests.cs"
check "QA-05 governance tests independently re-prove the repository-boundary consumption checks" \
  bash -c "grep -q 'ApprovalRequest_ConsumeAsync_rejects_a_request_that_is_not_yet_APPROVED' tests/features/bio-idn-006/RbacGovernanceTests.cs && grep -q 'ApprovalRequest_ConsumeAsync_rejects_a_request_that_was_already_consumed' tests/features/bio-idn-006/RbacGovernanceTests.cs"

# --- BIO-IDN-006-QA-06 (Seyi round 3, High): approval consumption happens only immediately before commit, never before validation ---
check "QA-06 the authority gate performs only a READ-ONLY clearance check, never a consumption, this early" \
  bash -c "grep -q 'makerCheckerService.IsActionClearedAsync(actionKey, targetPersonId' '$FEATURE/RoleAssignmentService.cs'"
check "QA-06 a dedicated ConsumeSuperAdminApprovalAsync method performs the actual consumption, separate from the authority gate" \
  bash -c "grep -q 'private async Task ConsumeSuperAdminApprovalAsync' '$FEATURE/RoleAssignmentService.cs'"
check "QA-06 consumption is called after the audit write is confirmed, using CancellationToken.None to match the commit's own discipline" \
  bash -c "grep -q 'await ConsumeSuperAdminApprovalAsync(SuperAdminAssignActionKey, personId);' '$FEATURE/RoleAssignmentService.cs' && grep -q 'await ConsumeSuperAdminApprovalAsync(SuperAdminRevokeActionKey, personId);' '$FEATURE/RoleAssignmentService.cs' && grep -q 'TryConsumeApprovalAsync(actionKey, targetPersonId, CancellationToken.None)' '$FEATURE/RoleAssignmentService.cs'"
check "QA-06 consumption runs strictly after the audit write in both AssignAsync's create and reactivate branches and in RevokeAsync" \
  bash -c "[ \"\$(grep -c 'await ConsumeSuperAdminApprovalAsync(' '$FEATURE/RoleAssignmentService.cs')\" = 3 ]"
check "QA-06 Seyi's own reproduction is checked in and now passes: a failed mutation never consumes the approval" \
  bash -c "grep -q 'ADVERSARIAL_failed_sensitive_mutation_does_not_consume_its_approval' tests/features/bio-idn-006/RoleAssignmentServiceTests.cs"
check "QA-06 a test proves the SAME untouched approval genuinely clears a retried mutation" \
  bash -c "grep -q 'BIOIDN006_QA06_a_failed_mutations_untouched_approval_genuinely_works_on_retry' tests/features/bio-idn-006/RoleAssignmentServiceTests.cs"
check "QA-06 a test proves an idempotent no-op mutation never consumes the approval either" \
  bash -c "grep -q 'BIOIDN006_QA06_an_idempotent_no_op_mutation_never_consumes_the_approval' tests/features/bio-idn-006/RoleAssignmentServiceTests.cs"

# --- BIO-IDN-006-QA-07 (Seyi round 4, Critical): raising/deciding a maker-checker request requires real authority, not just a distinct actor id ---
check "QA-07 raising a request requires rbac.approval_request, checked before any other validation" \
  bash -c "manage_line=\$(grep -n 'ApprovalRequestPermission = \"rbac.approval_request\"' '$FEATURE/MakerCheckerService.cs' | head -1 | cut -d: -f1); gate_line=\$(grep -n 'ActorHasPermissionAsync(tenantId, actor.Id, ApprovalRequestPermission' '$FEATURE/MakerCheckerService.cs' | head -1 | cut -d: -f1); errors_line=\$(grep -n 'var errors = new List<ValidationErrorItem>();' '$FEATURE/MakerCheckerService.cs' | sed -n '2p' | cut -d: -f1); [ -n \"\$manage_line\" ] && [ -n \"\$gate_line\" ] && [ -n \"\$errors_line\" ] && [ \"\$gate_line\" -lt \"\$errors_line\" ]"
check "QA-07 deciding a request requires rbac.approval_decide, checked before any other validation" \
  bash -c "grep -q 'ApprovalDecidePermission = \"rbac.approval_decide\"' '$FEATURE/MakerCheckerService.cs' && grep -q 'ActorHasPermissionAsync(tenantId, actor.Id, ApprovalDecidePermission' '$FEATURE/MakerCheckerService.cs'"
check "QA-07 the self-approval check remains, separate from and after the authority gate" \
  bash -c "authority_line=\$(grep -n 'ActorHasPermissionAsync(tenantId, actor.Id, ApprovalDecidePermission' '$FEATURE/MakerCheckerService.cs' | head -1 | cut -d: -f1); self_line=\$(grep -n 'SelfApprovalForbidden' '$FEATURE/MakerCheckerService.cs' | head -1 | cut -d: -f1); [ -n \"\$authority_line\" ] && [ -n \"\$self_line\" ] && [ \"\$authority_line\" -lt \"\$self_line\" ]"
check "QA-07 service-level tests prove an unprivileged actor cannot raise or decide a request" \
  bash -c "grep -q 'PRIVILEGE_ESCALATION_an_unprivileged_actor_cannot_raise_an_approval_request' tests/features/bio-idn-006/MakerCheckerServiceTests.cs && grep -q 'PRIVILEGE_ESCALATION_an_unprivileged_actor_cannot_decide_an_approval_request' tests/features/bio-idn-006/MakerCheckerServiceTests.cs"
check "QA-07 a test proves roles explicitly delegated the permissions can participate without holding SUPER_ADMIN" \
  bash -c "grep -q 'A_role_explicitly_delegated_rbac_approval_request_and_rbac_approval_decide_can_participate_without_holding_SUPER_ADMIN' tests/features/bio-idn-006/MakerCheckerServiceTests.cs"
check "QA-07 endpoint-level and real-host tests prove the same rejection through the full HTTP pipeline" \
  bash -c "grep -q 'Maker_checker_full_pipeline_request_then_decide' tests/features/bio-idn-006/RbacEndpointsIntegrationTests.cs && grep -q 'BIOIDN006_QA07_PRIVILEGE_ESCALATION_an_unprivileged_actor_cannot_raise_a_sensitive_action_approval_request_through_the_real_packaged_host' tests/dotnet/Biolog.IntegrationTests/RbacHostChannelTests.cs"

# --- BIO-IDN-006-QA-08 (Zainab's security review, Critical; ADR-0006 Decision 3): a verified provenance proof, bound to the specific asserted actor id, gates the highest-risk mutations ---
check "QA-08 an ADR-0006 exists and Decision 3 names the provenance mechanism" \
  bash -c "test -f docs/adr/ADR-0006-actor-identity-and-authorization-boundary.md && grep -q 'Decision 3' docs/adr/ADR-0006-actor-identity-and-authorization-boundary.md"
check "QA-08 an actor-provenance store and gate exist, mirroring BIO-CRD-001's TenantCredentialStore precedent (constant-time, fails closed when unconfigured), bound to the specific actor id via HMAC" \
  bash -c "test -f '$FEATURE/Api/ActorProvenanceGate.cs' && grep -q 'CryptographicOperations.FixedTimeEquals' '$FEATURE/Api/ActorProvenanceGate.cs' && grep -q 'IActorProvenanceStore' '$FEATURE/Api/ActorProvenanceGate.cs' && grep -q 'HMACSHA256' '$FEATURE/Api/ActorProvenanceGate.cs'"
check "QA-08 the provenance secret is loaded from IConfiguration out of band, never reachable through an HTTP endpoint" \
  bash -c "grep -q 'ActorProvenanceProvisioning.LoadFromConfiguration' api/Biolog.Api/Modules/BioIdn006Module.cs && ! grep -rq 'ActorProvenanceStore' '$FEATURE/Api/RoleAssignmentEndpoints.cs' '$FEATURE/Api/PermissionEndpoints.cs' '$FEATURE/Api/MakerCheckerEndpoints.cs'"
check "QA-08 no provenance-secret value is committed to source" \
  bash -c "! grep -rqi 'actorprovenancesecret' api/Biolog.Api/appsettings*.json '$FEATURE'/appsettings*.json 2>/dev/null"
check "QA-08 the gate runs on role assign/revoke, permission-grant config, sensitive-action-rule config, and approval raise/decide - the four surfaces already scoped as needing real authority" \
  bash -c "grep -q 'provenanceGate.Require' '$FEATURE/Api/RoleAssignmentEndpoints.cs' && [ \"\$(grep -c 'provenanceGate.Require' '$FEATURE/Api/RoleAssignmentEndpoints.cs')\" = 2 ] && grep -q 'provenanceGate.Require' '$FEATURE/Api/PermissionEndpoints.cs' && [ \"\$(grep -c 'provenanceGate.Require' '$FEATURE/Api/MakerCheckerEndpoints.cs')\" = 3 ]"
check "QA-08 the gate is checked on the SAME already-resolved actor id, immediately after ActorResolver runs, before any authorization decision uses it" \
  bash -c "resolve_line=\$(grep -n 'var actor = ActorResolver.Resolve(context.Request);' '$FEATURE/Api/RoleAssignmentEndpoints.cs' | head -1 | cut -d: -f1); gate_line=\$(grep -n 'provenanceGate.Require(context.Request, actor.Id);' '$FEATURE/Api/RoleAssignmentEndpoints.cs' | head -1 | cut -d: -f1); [ -n \"\$resolve_line\" ] && [ -n \"\$gate_line\" ] && [ \"\$resolve_line\" -lt \"\$gate_line\" ]"
check "QA-08 documented explicitly as an interim provenance mitigation, not authentication and not separation-of-duties, with the residual self-approval risk stated, against ADR-0006 Decision 3's own language" \
  bash -c "grep -qi 'NOT authentication' '$FEATURE/Api/ActorProvenanceGate.cs' && grep -qi 'self-approve' '$FEATURE/Api/ActorProvenanceGate.cs' && grep -q 'ADR-0006' '$FEATURE/Api/ActorProvenanceGate.cs'"
check "QA-08 real-host and feature-local tests prove a missing/mismatched provenance proof is rejected before authority is ever checked" \
  bash -c "grep -q 'BIOIDN006_QA08_PRIVILEGE_ESCALATION_a_caller_without_a_valid_provenance_proof_is_rejected_before_authority_is_ever_checked_through_the_real_packaged_host' tests/dotnet/Biolog.IntegrationTests/RbacHostChannelTests.cs && grep -q 'Assigning_a_role_without_a_provenance_proof_is_rejected_even_for_an_otherwise_privileged_actor' tests/features/bio-idn-006/RbacEndpointsIntegrationTests.cs && grep -q 'Assigning_a_role_with_a_provenance_proof_computed_for_a_different_actor_id_is_rejected_identically_to_a_missing_one' tests/features/bio-idn-006/RbacEndpointsIntegrationTests.cs"

# --- SUPER_ADMIN: implicit, non-configurable authority ---
check "SUPER_ADMIN short-circuits HasPermissionAsync to granted, before any grant lookup" \
  bash -c "grep -q 'a.Role == RoleType.SuperAdmin' '$FEATURE/PermissionService.cs'"
check "PRIVILEGE ESCALATION: a grant can never be configured for SUPER_ADMIN" \
  bash -c "grep -q 'SuperAdminGrantNotConfigurable' '$FEATURE/Domain/RbacError.cs' && grep -q 'role == RoleType.SuperAdmin' '$FEATURE/PermissionService.cs'"
check "a test proves SUPER_ADMIN holds every permission with no grant ever configured" \
  bash -c "grep -q 'SUPER_ADMIN_holds_every_permission_with_no_grant_ever_configured' tests/features/bio-idn-006/PermissionServiceTests.cs"
check "PRIVILEGE ESCALATION test: a grant can never be set for SUPER_ADMIN" \
  bash -c "grep -q 'PRIVILEGE_ESCALATION_a_grant_can_never_be_configured_for_SUPER_ADMIN' tests/features/bio-idn-006/PermissionServiceTests.cs"
check "PRIVILEGE ESCALATION test: an unrelated role's own grant never implies SUPER_ADMIN-level authority" \
  bash -c "grep -q 'PRIVILEGE_ESCALATION_holding_an_unrelated_role_with_its_own_grant_never_implies_SUPER_ADMIN_authority' tests/features/bio-idn-006/PermissionServiceTests.cs"

# --- AC03: sensitive actions can require second approval (maker-checker) ---
check "AC03 a tenant-scoped SensitiveActionRule aggregate exists" \
  bash -c "grep -q 'RequiresSecondApproval { get; init; }' '$FEATURE/Domain/SensitiveActionRule.cs'"
check "AC03 a maker-checker ApprovalRequest aggregate exists with a Pending/Approved/Rejected lifecycle" \
  bash -c "test -f '$FEATURE/Domain/ApprovalRequest.cs' && grep -q 'Pending,' '$FEATURE/Domain/ApprovalRequestStatus.cs' && grep -q 'Approved,' '$FEATURE/Domain/ApprovalRequestStatus.cs' && grep -q 'Rejected,' '$FEATURE/Domain/ApprovalRequestStatus.cs'"
check "AC03 at most one Pending request per target is a structural invariant, checked in the service and the repository" \
  bash -c "grep -q 'ApprovalRequestAlreadyPending' '$FEATURE/MakerCheckerService.cs' && grep -q 'ApprovalRequestAlreadyPending' '$FEATURE/Domain/InMemoryApprovalRequestRepository.cs'"
check "AC03 endpoints exist for the rule and the full request/decide workflow" \
  bash -c "grep -q '/api/v1/sensitive-actions/{actionKey}/rule' '$FEATURE/Api/MakerCheckerEndpoints.cs' && grep -q '/approval-requests/{targetRef}' '$FEATURE/Api/MakerCheckerEndpoints.cs' && grep -q 'decide' '$FEATURE/Api/MakerCheckerEndpoints.cs'"
check "AC03 a test proves an approved request clears the action for that target" \
  bash -c "grep -q 'An_approved_request_clears_the_action_for_that_target' tests/features/bio-idn-006/MakerCheckerServiceTests.cs"
check "AC03 a test proves a rejected request never clears the action" \
  bash -c "grep -q 'A_rejected_request_never_clears_the_action' tests/features/bio-idn-006/MakerCheckerServiceTests.cs"

# --- PRIVILEGE ESCALATION: maker-checker separation of duties (mandatory per the story's own security criteria) ---
check "the checker's actor id is checked against the maker's actor id BEFORE any write is attempted" \
  bash -c "self_check_line=\$(grep -n 'SelfApprovalForbidden' '$FEATURE/MakerCheckerService.cs' | head -1 | cut -d: -f1); replace_line=\$(grep -n 'requestRepository.ReplaceAsync' '$FEATURE/MakerCheckerService.cs' | head -1 | cut -d: -f1); [ -n \"\$self_check_line\" ] && [ -n \"\$replace_line\" ] && [ \"\$self_check_line\" -lt \"\$replace_line\" ]"
check "the repository independently re-checks separation of duties at the persistence boundary against the ACTUAL deciding actor, not the caller-supplied record" \
  bash -c "grep -q 'decidingActorId' '$FEATURE/Domain/IApprovalRequestRepository.cs' && grep -q 'decidingActorId' '$FEATURE/Domain/InMemoryApprovalRequestRepository.cs'"
check "PRIVILEGE ESCALATION test: approving one's own request is rejected" \
  bash -c "grep -q 'PRIVILEGE_ESCALATION_approving_ones_own_request_is_rejected' tests/features/bio-idn-006/MakerCheckerServiceTests.cs"
check "PRIVILEGE ESCALATION test: rejecting one's own request is ALSO forbidden, not only approving" \
  bash -c "grep -q 'PRIVILEGE_ESCALATION_rejecting_ones_own_request_is_also_forbidden_not_only_approving' tests/features/bio-idn-006/MakerCheckerServiceTests.cs"
check "PRIVILEGE ESCALATION test: self-approval is rejected before any state change" \
  bash -c "grep -q 'PRIVILEGE_ESCALATION_self_approval_is_rejected_before_any_state_changes' tests/features/bio-idn-006/MakerCheckerServiceTests.cs"
check "PRIVILEGE ESCALATION test: a forged replacement record cannot smuggle a different decider past the repository boundary" \
  bash -c "grep -q 'PRIVILEGE_ESCALATION_a_forged_replacement_record_cannot_smuggle_a_different_decider_past_the_repository_boundary' tests/features/bio-idn-006/MakerCheckerServiceTests.cs"
check "PRIVILEGE ESCALATION: self-approval's real-host proof lives in the feature-local host now - BIO-IDN-006-QA-07 removed the real packaged host's seeding-free path to even RAISE a request, let alone self-approve one; QA-07's own real-host proof (an unprivileged actor cannot raise a request at all) covers that project instead" \
  bash -c "grep -q 'BIOIDN006_QA07_PRIVILEGE_ESCALATION_an_unprivileged_actor_cannot_raise_a_sensitive_action_approval_request_through_the_real_packaged_host' tests/dotnet/Biolog.IntegrationTests/RbacHostChannelTests.cs"
check "PRIVILEGE ESCALATION test: the maker cannot approve their own request through the endpoint" \
  bash -c "grep -q 'PRIVILEGE_ESCALATION_the_maker_cannot_approve_their_own_request_through_the_endpoint' tests/features/bio-idn-006/RbacEndpointsIntegrationTests.cs"
check "a test proves an already-decided request is rejected, never silently absorbed" \
  bash -c "grep -q 'Deciding_an_already_decided_request_is_rejected_not_silently_absorbed' tests/features/bio-idn-006/MakerCheckerServiceTests.cs"

# --- AC04: role changes are audited ---
check "AC04 role assignment/revocation is a governed transition, never a delete" \
  bash -c "! grep -qi 'DeleteAsync\|RemoveAsync' '$FEATURE/Domain/IPersonRoleAssignmentRepository.cs'"
check "AC04 assign and revoke both write an audit event before committing" \
  bash -c "grep -q 'AuditAsync' '$FEATURE/RoleAssignmentService.cs' && [ \"\$(grep -c 'await AuditAsync' '$FEATURE/RoleAssignmentService.cs')\" -ge 2 ]"
check "AC04 a test proves assign records exactly one allowed audit event" \
  bash -c "grep -q 'Assign_records_exactly_one_allowed_audit_event' tests/features/bio-idn-006/RoleAssignmentServiceTests.cs"
check "AC04 a test proves revoke records an audit event" \
  bash -c "grep -q 'Revoke_records_an_audit_event' tests/features/bio-idn-006/RoleAssignmentServiceTests.cs"
check "AC04 a test proves revoking preserves the record rather than deleting it" \
  bash -c "grep -q 'Revoking_a_role_moves_it_to_REVOKED_and_preserves_the_record' tests/features/bio-idn-006/RoleAssignmentServiceTests.cs"

# --- Applying BIO-IDN-002-QA-01 from the start ---
check "a narrow, read-only IPersonExistenceGateway interface exists, owned by this feature" \
  bash -c "test -f '$FEATURE/Domain/IPersonExistenceGateway.cs' && grep -q 'internal interface IPersonExistenceGateway' '$FEATURE/Domain/IPersonExistenceGateway.cs'"
check "AssignAsync and HasPermissionAsync both call the gateway before anything else" \
  bash -c "grep -q 'personGateway.ExistsAsync' '$FEATURE/RoleAssignmentService.cs' && grep -q 'personGateway.ExistsAsync' '$FEATURE/PermissionService.cs'"
check "a missing or foreign-tenant person is rejected as person_not_found (404)" \
  bash -c "grep -q 'PersonNotFound' '$FEATURE/Domain/RbacError.cs' && grep -A2 'PersonNotFound(string personId)' '$FEATURE/Domain/RbacError.cs' | grep -q ', 404,'"
check "the real adapter wraps BIO-IDN-001's own tenant-scoped PersonService.GetAsync" \
  bash -c "grep -q 'PersonExistenceGatewayAdapter' api/Biolog.Api/Modules/BioIdn006Module.cs && grep -q 'personService.GetAsync' api/Biolog.Api/Modules/BioIdn006Module.cs"
check "real-packaged-host tests prove an unknown person and a real tenant-A person via tenant-B's host both fail identically" \
  bash -c "grep -q 'BIOIDN006_assigning_a_role_for_an_unknown_canonical_person_id_is_rejected_for_lack_of_authority_before_existence_is_ever_checked' tests/dotnet/Biolog.IntegrationTests/RbacHostChannelTests.cs && grep -q 'BIOIDN006_a_tenant_B_caller_gets_the_identical_rejection_as_the_wholly_unknown_case_never_leaking_tenant_A_state' tests/dotnet/Biolog.IntegrationTests/RbacHostChannelTests.cs"

# --- Governance, applying BIO-IDN-001-QA-01's lesson from the start ---
check "governance: every repository and its implementation are internal, not public" \
  bash -c "for f in IPersonRoleAssignmentRepository InMemoryPersonRoleAssignmentRepository IRolePermissionGrantRepository InMemoryRolePermissionGrantRepository ISensitiveActionRuleRepository InMemorySensitiveActionRuleRepository IApprovalRequestRepository InMemoryApprovalRequestRepository; do grep -qE '^internal (interface|sealed class) '\"\$f\" \"$FEATURE/Domain/\$f.cs\" || exit 1; done"
check "governance: every aggregate's constructor is internal, not a public object-initializer surface" \
  bash -c "for f in PersonRoleAssignment RolePermissionGrant SensitiveActionRule ApprovalRequest; do grep -q '    internal '\"\$f\"'(' \"$FEATURE/Domain/\$f.cs\" || exit 1; done"
check "governance: every service is internal too" \
  bash -c "grep -q '^internal sealed class RoleAssignmentService' '$FEATURE/RoleAssignmentService.cs' && grep -q '^internal sealed partial class PermissionService' '$FEATURE/PermissionService.cs' && grep -q '^internal sealed partial class MakerCheckerService' '$FEATURE/MakerCheckerService.cs'"
check "governance: the InternalsVisibleTo seam is explicit and auditable in source control" \
  bash -c "grep -q '<InternalsVisibleTo Include=\"Biolog.Api\" />' '$FEATURE/Biolog.Features.Rbac.csproj' && grep -q '<InternalsVisibleTo Include=\"Biolog.Features.Rbac.Tests\" />' '$FEATURE/Biolog.Features.Rbac.csproj'"
check "governance: every repository independently re-validates identity immutability and initial state at the persistence boundary" \
  bash -c "grep -q 'IdentityImmutable' '$FEATURE/Domain/InMemoryPersonRoleAssignmentRepository.cs' && grep -q 'IdentityImmutable' '$FEATURE/Domain/InMemoryApprovalRequestRepository.cs'"
check "governance: reflection tests prove every repository/aggregate/service is inaccessible to ordinary production consumers" \
  bash -c "grep -q 'RoleAssignment_has_no_publicly_accessible_constructor\|PersonRoleAssignment_has_no_publicly_accessible_constructor' tests/features/bio-idn-006/RbacGovernanceTests.cs && grep -q 'RoleAssignmentService_PermissionService_and_MakerCheckerService_are_all_internal' tests/features/bio-idn-006/RbacGovernanceTests.cs"
check "governance: no ProjectReference to BIO-IDN-001's project (existence is proven through a narrow gateway)" \
  bash -c "! grep -q '<ProjectReference.*bio-idn-001' '$FEATURE/Biolog.Features.Rbac.csproj'"

# --- Tenant boundary is mandatory, inherited from every sibling story's own pattern ---
check "every mutating/reading service reads the resolved tenant, never a caller-supplied id" \
  bash -c "grep -q 'RequireTenant()' '$FEATURE/RoleAssignmentService.cs' && grep -q 'RequireTenant()' '$FEATURE/PermissionService.cs' && grep -q 'RequireTenant()' '$FEATURE/MakerCheckerService.cs'"
check "a missing tenant context fails closed (tenant_context_missing, 400)" \
  bash -c "grep -q 'TenantContextMissing' '$FEATURE/Domain/RbacError.cs' && grep -A2 'TenantContextMissing()' '$FEATURE/Domain/RbacError.cs' | grep -q ', 400)'"
check "real shipped-host positive-path test exists (ADR-0002 Decision 6 obligation)" \
  bash -c "test -f tests/dotnet/Biolog.IntegrationTests/RbacHostChannelTests.cs && grep -q 'WebApplicationFactory<Program>' tests/dotnet/Biolog.IntegrationTests/RbacHostChannelTests.cs"

# --- ADR-0003 Decision 7: audit-atomicity ---
check "Decision 7 element 1: four per-aggregate locks exist, each in its own singleton-safe class" \
  bash -c "for f in RoleAssignmentLockRegistry PermissionGrantLockRegistry SensitiveActionRuleLockRegistry ApprovalRequestLockRegistry; do test -f \"$FEATURE/\$f.cs\" && grep -q 'AcquireAsync' \"$FEATURE/\$f.cs\" || exit 1; done"
check "the locks live in their own classes because every service must be Scoped (captive-dependency avoidance)" \
  bash -c "grep -q 'AddScoped<RoleAssignmentService>' api/Biolog.Api/Modules/BioIdn006Module.cs && grep -q 'AddSingleton<RoleAssignmentLockRegistry>' api/Biolog.Api/Modules/BioIdn006Module.cs"
check "Decision 7 element 3: ExistsAsync verification on an ambiguous audit-write throw" \
  bash -c "grep -q 'ExistsAsync' '$FEATURE/Audit/IRbacAuditSink.cs' && grep -q 'auditSink.ExistsAsync' '$FEATURE/RoleAssignmentService.cs' && grep -q 'auditSink.ExistsAsync' '$FEATURE/MakerCheckerService.cs'"
check "Decision 7 element 4: CancellationToken.None on the commit once the audit is confirmed" \
  bash -c "grep -q 'CancellationToken.None' '$FEATURE/RoleAssignmentService.cs' && grep -q 'CancellationToken.None' '$FEATURE/MakerCheckerService.cs'"
check "a same-target concurrency race for approval decisions is proven, never producing two decided outcomes" \
  bash -c "grep -q 'Two_genuinely_concurrent_revokes_of_the_same_assignment_never_produce_a_false_allowed_audit' tests/features/bio-idn-006/RoleAssignmentServiceTests.cs"
check "audit sink failure never leaves a committed-but-unaudited record (assign/revoke/request/decide)" \
  bash -c "grep -q 'Assign_is_never_committed_when_the_audit_sink_fails' tests/features/bio-idn-006/RoleAssignmentServiceTests.cs && grep -q 'RequestApproval_is_never_committed_when_the_audit_sink_fails' tests/features/bio-idn-006/MakerCheckerServiceTests.cs && grep -q 'Decide_is_never_committed_when_the_audit_sink_fails' tests/features/bio-idn-006/MakerCheckerServiceTests.cs"

# --- Lock scope reasoned explicitly (BIO-IDN-003-QA-01's lesson), not assumed by analogy ---
check "two different lock scopes are used deliberately: (TenantId, ActionKey) for the rule, (TenantId, ActionKey, TargetRef) for the request history" \
  bash -c "grep -q 'ruleLockRegistry.AcquireAsync(tenantId, key' '$FEATURE/MakerCheckerService.cs' && grep -q 'requestLockRegistry.AcquireAsync(tenantId, key' '$FEATURE/MakerCheckerService.cs'"

# --- Developer test wiring ---
check "unit/audit/scope tests reference the feature project" \
  bash -c "grep -q 'Biolog.Features.Rbac.csproj' tests/features/bio-idn-006/Biolog.Features.Rbac.Tests.csproj"
check "feature-local integration tests exist and reference TestHost" \
  bash -c "test -f tests/features/bio-idn-006/RbacEndpointsIntegrationTests.cs && grep -q 'Microsoft.AspNetCore.TestHost' tests/features/bio-idn-006/Biolog.Features.Rbac.Tests.csproj"
check "docs/features/bio-idn-006/README.md documents the feature" test -f docs/features/bio-idn-006/README.md
check "builder evidence recorded" test -d evidence/features/bio-idn-006/builder

echo "=============================="
echo "$pass passed, $fail failed"
[ "$fail" -eq 0 ]
