#!/usr/bin/env bash
# Offline-safe structural checks for BIO-ACC-001 (access policy model and assurance levels).
# Behavioural assertions (rule matching, draft/publish versioning, deny precedence, audit
# atomicity, HTTP pipeline) live in the executable xunit suites under
# tests/features/bio-acc-001/ and
# tests/dotnet/Biolog.IntegrationTests/AccessPolicyHostChannelTests.cs (see
# docs/features/bio-acc-001/README.md).
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"
pass=0; fail=0
check() { local label="$1"; shift; if "$@"; then echo "PASS  $label"; pass=$((pass+1)); else echo "FAIL  $label"; fail=$((fail+1)); fi; }

FEATURE=api/features/bio-acc-001

# --- Project shape ---
check "feature project exists" test -f "$FEATURE/Biolog.Features.AccessPolicy.csproj"
check "project targets the pinned .NET SDK's TFM" bash -c "grep -q '<TargetFramework>net10.0</TargetFramework>' '$FEATURE/Biolog.Features.AccessPolicy.csproj'"
check "module lives one-way in api/Biolog.Api/Modules, not back in the feature project" \
  bash -c "test -f api/Biolog.Api/Modules/BioAcc001Module.cs && ! test -f '$FEATURE/BioAcc001Module.cs'"
check "registered in the packaged Program.cs module array" \
  bash -c "grep -q 'new BioAcc001Module()' api/Biolog.Api/Program.cs"
check "Biolog.Api.csproj references the feature project" \
  bash -c "grep -q 'features/bio-acc-001/Biolog.Features.AccessPolicy.csproj' api/Biolog.Api/Biolog.Api.csproj"
check "no ProjectReference from the feature project to any other feature project or Biolog.Api" \
  bash -c "! grep -q '<ProjectReference' '$FEATURE/Biolog.Features.AccessPolicy.csproj'"

# --- AC01: the policy model supports person/profile, credential class, location, schedule, identity/card status, assurance level ---
check "AC01 SubjectMatch supports ANY_PERSON, SPECIFIC_PERSON and PROFILE_KEY" \
  bash -c "grep -q 'AnyPerson' '$FEATURE/Domain/PolicyMatchTypes.cs' && grep -q 'SpecificPerson' '$FEATURE/Domain/PolicyMatchTypes.cs' && grep -q 'ProfileKey' '$FEATURE/Domain/PolicyMatchTypes.cs'"
check "AC01 CredentialClassMatch exists" bash -c "grep -q 'record CredentialClassMatch' '$FEATURE/Domain/PolicyMatchTypes.cs'"
check "AC01 LocationMatch exists" bash -c "grep -q 'record LocationMatch' '$FEATURE/Domain/PolicyMatchTypes.cs'"
check "AC01 ScheduleMatch supports recurring weekly windows" bash -c "grep -q 'record ScheduleWindow' '$FEATURE/Domain/PolicyMatchTypes.cs'"
check "AC01 IdentityStatusMatch and CardStatusMatch both exist" \
  bash -c "grep -q 'record IdentityStatusMatch' '$FEATURE/Domain/PolicyMatchTypes.cs' && grep -q 'record CardStatusMatch' '$FEATURE/Domain/PolicyMatchTypes.cs'"
check "AC01 AssuranceLevel is a three-level ordered enum" \
  bash -c "grep -q 'Basic,' '$FEATURE/Domain/AssuranceLevel.cs' && grep -q 'Elevated,' '$FEATURE/Domain/AssuranceLevel.cs' && grep -q 'High,' '$FEATURE/Domain/AssuranceLevel.cs'"
check "AC01 a specific-person subject is existence-checked at authoring time" \
  bash -c "grep -q 'IPersonExistenceGateway' '$FEATURE/AccessPolicyService.cs' && grep -q 'SubjectPersonNotFound' '$FEATURE/AccessPolicyService.cs'"
check "AC01 identity status is resolved live via a gateway, never accepted as a caller-supplied evaluation input" \
  bash -c "grep -q 'IIdentityStatusGateway' '$FEATURE/Domain/PolicyEvaluation.cs' && ! grep -q 'PresentedIdentityStatus' '$FEATURE/Domain/PolicyEvaluation.cs'"
check "AC01 a test proves a rule can name every AC01 dimension at once" \
  bash -c "grep -q 'Creating_a_draft_with_a_rule_naming_every_AC01_dimension_succeeds' tests/features/bio-acc-001/AccessPolicyServiceTests.cs"

# --- AC02: draft/publish versioning ---
check "AC02 AccessPolicyStatus is a Draft/Published workflow" \
  bash -c "grep -q 'Draft,' '$FEATURE/Domain/AccessPolicyStatus.cs' && grep -q 'Published,' '$FEATURE/Domain/AccessPolicyStatus.cs'"
check "AC02 revising a draft edits in place while Draft, starts a new version once Published" \
  bash -c "grep -q 'ReviseDraft' '$FEATURE/Domain/AccessPolicy.cs'"
check "AC02 publish requires a different actor from the draft's own author (maker-checker)" \
  bash -c "grep -q 'SelfApprovalForbidden' '$FEATURE/Domain/AccessPolicy.cs'"
check "AC02 publish requires the caller to hold a distinct permission from draft management" \
  bash -c "grep -q 'PublishPermission' '$FEATURE/AccessPolicyService.cs' && grep -q 'ManagePermission' '$FEATURE/AccessPolicyService.cs'"
check "AC02 a test proves revising in place keeps the same version number" \
  bash -c "grep -q 'Revising_a_draft_edits_the_same_version_in_place' tests/features/bio-acc-001/AccessPolicyServiceTests.cs"
check "AC02 a test proves revising a published policy starts a new draft version, leaving the published one untouched" \
  bash -c "grep -q 'Revising_a_published_policy_starts_a_new_draft_version_leaving_the_published_one_untouched' tests/features/bio-acc-001/AccessPolicyServiceTests.cs"
check "AC02 a test proves self-approval is rejected" \
  bash -c "grep -q 'Publishing_by_the_drafts_own_author_is_rejected_maker_checker' tests/features/bio-acc-001/AccessPolicyServiceTests.cs"

# --- AC03: deny precedence is explicit ---
check "AC03 PolicyRule.Effect is ALLOW or DENY, a per-rule field" \
  bash -c "grep -q 'enum PolicyRuleEffect' '$FEATURE/Domain/PolicyRule.cs'"
check "AC03 the evaluator checks every DENY match before considering any ALLOW match" \
  bash -c "deny_line=\$(grep -n 'denyMatch = matches' '$FEATURE/Domain/PolicyEvaluation.cs' | head -1 | cut -d: -f1); allow_line=\$(grep -n 'allowMatches = matches' '$FEATURE/Domain/PolicyEvaluation.cs' | head -1 | cut -d: -f1); [ -n \"\$deny_line\" ] && [ -n \"\$allow_line\" ] && [ \"\$deny_line\" -lt \"\$allow_line\" ]"
check "AC03 the default outcome with no matching rule is DENY (fail-closed)" \
  bash -c "grep -q 'no_matching_rule' '$FEATURE/Domain/PolicyEvaluation.cs'"
check "AC03 insufficient assurance on a matching ALLOW rule is its own distinct, auditable reason" \
  bash -c "grep -q 'insufficient_assurance' '$FEATURE/Domain/PolicyEvaluation.cs'"
check "AC03 a test proves deny wins even when the deny rule appears after the allow rule in the list" \
  bash -c "grep -q 'A_matching_deny_rule_wins_over_a_matching_allow_rule_regardless_of_list_order' tests/features/bio-acc-001/PolicyEvaluationServiceTests.cs"
check "AC03 a test proves deny wins even when it appears before the allow rule too (order genuinely does not matter)" \
  bash -c "grep -q 'A_matching_deny_rule_wins_even_when_it_appears_before_the_allow_rule' tests/features/bio-acc-001/PolicyEvaluationServiceTests.cs"
check "AC03 a test proves the default-deny fail-closed outcome" \
  bash -c "grep -q 'No_matching_rule_at_all_defaults_to_deny' tests/features/bio-acc-001/PolicyEvaluationServiceTests.cs"

# --- AC04: policy evaluation is auditable and testable ---
check "AC04 an audit sink exists, mirroring ADR-0003's append-only audit envelope" \
  bash -c "test -f '$FEATURE/Audit/IAccessPolicyAuditSink.cs' && grep -q 'ExistsAsync' '$FEATURE/Audit/IAccessPolicyAuditSink.cs'"
check "AC04 PolicyEvaluationService audits every evaluation before returning, following ADR-0003 Decision 7" \
  bash -c "grep -q 'AuditAsync' '$FEATURE/PolicyEvaluationService.cs' && grep -q 'AuditUnavailable' '$FEATURE/PolicyEvaluationService.cs'"
check "AC04 AccessPolicyService audits every mutation the same way" \
  bash -c "grep -q 'AuditAsync' '$FEATURE/AccessPolicyService.cs' && grep -q 'AuditUnavailable' '$FEATURE/AccessPolicyService.cs'"
check "AC04 the pure evaluator is independently testable with no service/gateway dependency" \
  bash -c "! grep -q 'Task' '$FEATURE/Domain/PolicyEvaluation.cs'"
check "AC04 a test proves a confirmed audit-sink failure prevents the mutation from ever committing" \
  bash -c "grep -q 'A_confirmed_audit_sink_failure_prevents_the_draft_from_ever_being_committed' tests/features/bio-acc-001/AccessPolicyServiceTests.cs"
check "AC04 a test proves the evaluation audit outcome matches the decision" \
  bash -c "grep -q 'Evaluating_writes_an_audit_event_whose_outcome_matches_the_decision' tests/features/bio-acc-001/PolicyEvaluationServiceTests.cs"

# --- RBAC integration + ADR-0006 provenance (this story's own scope, not retrofitted onto siblings) ---
check "RBAC: every authoring/publishing/evaluating method checks a permission via IPermissionGateway" \
  bash -c "grep -q 'IPermissionGateway' '$FEATURE/AccessPolicyService.cs' && grep -q 'IPermissionGateway' '$FEATURE/PolicyEvaluationService.cs'"
check "ADR-0006: ActorProvenanceGate exists and is referenced against the accepted ADR" \
  bash -c "test -f '$FEATURE/Api/ActorProvenanceGate.cs' && grep -q 'ADR-0006' '$FEATURE/Api/ActorProvenanceGate.cs'"
check "ADR-0006: the provenance store uses constant-time comparison" \
  bash -c "grep -q 'CryptographicOperations.FixedTimeEquals' '$FEATURE/Api/ActorProvenanceGate.cs'"
check "ADR-0006: every endpoint (mutating, evaluating AND read - QA-01) calls the provenance gate immediately after resolving the actor" \
  bash -c "[ \"\$(grep -c 'provenanceGate.Require(context.Request, actor.Id)' '$FEATURE/Api/AccessPolicyEndpoints.cs')\" = 6 ]"
check "ADR-0006: no provenance secret is committed to source" \
  bash -c "! grep -rq 'ActorProvenanceSecret' api/Biolog.Api/appsettings*.json 2>/dev/null"
check "a real-host test proves the provenance gate runs before authority is ever checked" \
  bash -c "grep -q 'ACC001_PRIVILEGE_ESCALATION_a_caller_without_a_valid_provenance_proof_is_rejected_before_authority_is_ever_checked_through_the_real_packaged_host' tests/dotnet/Biolog.IntegrationTests/AccessPolicyHostChannelTests.cs"

# --- BIO-ACC-001-QA-01 (Chioma's acceptance audit, High): reads require their own permission too ---
check "QA-01 a dedicated ReadPermission exists, distinct from Manage/Publish" \
  bash -c "grep -q 'ReadPermission = \"access_policy.read\"' '$FEATURE/AccessPolicyService.cs'"
check "QA-01 GetAsync and ListAsync both require an actor and check the read permission" \
  bash -c "grep -q 'GetAsync(string policyKey, AuditActor actor' '$FEATURE/AccessPolicyService.cs' && grep -q 'ListAsync(AuditActor actor' '$FEATURE/AccessPolicyService.cs'"
check "QA-01 both read endpoint handlers pass the resolved actor through to the service (not an unauthenticated read)" \
  bash -c "get_handler=\$(sed -n '/private static Task GetAsync/,/^    });/p' '$FEATURE/Api/AccessPolicyEndpoints.cs'); list_handler=\$(sed -n '/private static Task ListAsync/,/^    });/p' '$FEATURE/Api/AccessPolicyEndpoints.cs'); echo \"\$get_handler\" | grep -q 'service.GetAsync(policyKey, actor' && echo \"\$list_handler\" | grep -q 'service.ListAsync(actor'"
check "QA-01 a service-level test proves GET is rejected without the read permission, even for the policy's own author" \
  bash -c "grep -q 'Getting_a_policy_without_the_read_permission_is_rejected_even_for_the_policys_own_author' tests/features/bio-acc-001/AccessPolicyServiceTests.cs"
check "QA-01 a service-level test proves LIST is rejected without the read permission" \
  bash -c "grep -q 'Listing_policies_without_the_read_permission_is_rejected' tests/features/bio-acc-001/AccessPolicyServiceTests.cs"
check "QA-01 a positive-path test proves a read-only permission holder can read but not author or publish" \
  bash -c "grep -q 'A_caller_holding_only_the_read_permission_can_get_and_list_but_not_author_or_publish' tests/features/bio-acc-001/AccessPolicyServiceTests.cs"
check "QA-01 a real-host test proves an unprivileged actor cannot list policies through the shipped endpoint" \
  bash -c "grep -q 'BIOACC001_QA01_an_unprivileged_actor_cannot_list_policies_through_the_real_packaged_host' tests/dotnet/Biolog.IntegrationTests/AccessPolicyHostChannelTests.cs"

# --- Governance ---
check "governance test file exists" test -f tests/features/bio-acc-001/AccessPolicyGovernanceTests.cs
check "the feature project has no assembly reference to any sibling feature project (test-enforced)" \
  bash -c "grep -q 'This_project_has_no_assembly_reference_to_any_other_feature_project' tests/features/bio-acc-001/AccessPolicyGovernanceTests.cs"
check "feature-path-ownership.json declares this story's owned paths" \
  bash -c "grep -q 'BIO-ACC-001' governance/feature-path-ownership.json"

echo "----"
echo "BIO-ACC-001 structural checks: $pass passed, $fail failed"
[ "$fail" -eq 0 ]
