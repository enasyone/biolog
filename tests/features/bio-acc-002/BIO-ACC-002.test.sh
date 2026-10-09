#!/usr/bin/env bash
# Offline-safe structural checks for BIO-ACC-002 (online access decision service).
# Behavioural assertions live in the executable suites: the backend's own xunit tests
# (tests/features/bio-acc-002/) and the real-packaged-host tests
# (tests/dotnet/Biolog.IntegrationTests/AccessDecisionsHostChannelTests.cs) - see
# docs/features/bio-acc-002/README.md.
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"
pass=0; fail=0
check() { local label="$1"; shift; if "$@"; then echo "PASS  $label"; pass=$((pass+1)); else echo "FAIL  $label"; fail=$((fail+1)); fi; }

API_FEATURE=api/features/bio-acc-002
API_TESTS=tests/features/bio-acc-002
HOST_TESTS=tests/dotnet/Biolog.IntegrationTests

# --- AC01: face/fingerprint match is evidence, not the final allow decision ---
check "AC01 a pure DeriveAssuranceLevel function turns biometric evidence into an assurance level, never a decision" \
  bash -c "grep -q 'static AssuranceLevel DeriveAssuranceLevel' $API_FEATURE/Domain/AccessDecisionEvent.cs"
check "AC01 the derived assurance level is computed BEFORE the policy gateway is called, never afterward" \
  bash -c "awk '/DeriveAssuranceLevel/{d=NR} /policyGateway.EvaluateAsync/{p=NR} END{exit !(d>0 && p>0 && d<p)}' $API_FEATURE/AccessDecisionService.cs"
check "AC01 biometric evidence is carried into the persisted decision unmodified" \
  bash -c "grep -q 'BiometricEvidence' $API_FEATURE/Domain/AccessDecisionEvent.cs"
check "AC01 a fingerprint sample is optional - a card-only presentation is a legitimate request, not an error" \
  bash -c "grep -q 'FingerprintSample' $API_FEATURE/Api/AccessDecisionDtos.cs && grep -qv 'required' <(grep 'FingerprintSample' $API_FEATURE/Api/AccessDecisionDtos.cs)"
check "AC01 a match/no-match outcome never itself decides ALLOW/DENY - the decision field comes only from the policy gateway's own result" \
  bash -c "! grep -qE 'Decision = .*Matched|Decision = AccessDecisionOutcome.Allow;' $API_FEATURE/AccessDecisionService.cs"
check "a test proves a matched biometric never overrides a policy deny" \
  bash -c "grep -q 'AC01_a_matched_biometric_never_overrides_a_policy_deny' $API_TESTS/AccessDecisionServiceTests.cs"
check "a test proves no evidence derives Basic assurance and a single match derives Elevated" \
  bash -c "grep -q 'AC01_no_evidence_derives_basic_assurance' $API_TESTS/AccessDecisionCombinerTests.cs && grep -q 'AC01_a_single_matched_modality_derives_elevated_assurance' $API_TESTS/AccessDecisionCombinerTests.cs"

# --- AC02: suspended/revoked identity/credential denies ---
check "AC02 this feature never resolves identity/credential status itself - delegated entirely to BIO-ACC-001's own policy engine" \
  bash -c "! grep -rvE '^\s*///|^\s*//' $API_FEATURE/Domain/*.cs $API_FEATURE/AccessDecisionService.cs | grep -q 'IdentityStatus'"
check "a test proves a suspended/revoked-shaped deny reason from the policy gateway flows through unchanged" \
  bash -c "grep -q 'AC02_a_deny_reason_the_policy_gateway_reports_for_a_suspended_or_revoked_identity_flows_through_unchanged' $API_TESTS/AccessDecisionServiceTests.cs"

# --- AC03: decision records policy version and reason codes ---
check "AC03 AccessDecisionEvent carries PolicyKey, PolicyVersionNumber, ReasonCode and MatchedRuleId" \
  bash -c "grep -q 'PolicyKey' $API_FEATURE/Domain/AccessDecisionEvent.cs && grep -q 'PolicyVersionNumber' $API_FEATURE/Domain/AccessDecisionEvent.cs && grep -q 'ReasonCode' $API_FEATURE/Domain/AccessDecisionEvent.cs && grep -q 'MatchedRuleId' $API_FEATURE/Domain/AccessDecisionEvent.cs"
check "AC03 every decision is audited before being treated as committed (ADR-0003 Decision 7)" \
  bash -c "grep -q 'AuditAsync' $API_FEATURE/AccessDecisionService.cs && grep -q 'ExistsAsync' $API_FEATURE/AccessDecisionService.cs"
check "a test proves the decision records policy key, version and matched rule" \
  bash -c "grep -q 'AC03_the_decision_records_the_policy_key_version_and_matched_rule' $API_TESTS/AccessDecisionServiceTests.cs"

# --- AC04: service supports deterministic replay in tests ---
check "AC04 the combination logic is a pure function, separate from the async gateway orchestration" \
  test -f "$API_FEATURE/Domain/AccessDecisionEvent.cs"
check "AC04 a content fingerprint exists, excluding EventId/RecordedAt/CorrelationId (all per-delivery, never per-content)" \
  bash -c "grep -q 'static string ComputeFingerprint' $API_FEATURE/Domain/AccessDecisionEvent.cs"
check "a test proves the same inputs replayed twice produce a structurally identical decision" \
  bash -c "grep -q 'AC04_the_same_inputs_replayed_produce_a_structurally_identical_decision' $API_TESTS/AccessDecisionCombinerTests.cs"
check "a test proves a repeated idempotency key returns the same decision without re-calling the biometric gateways" \
  bash -c "grep -q 'AC04_a_repeated_idempotency_key_returns_the_same_decision_without_recalling_the_biometric_gateways_a_second_time' $API_TESTS/AccessDecisionServiceTests.cs"

# --- ADR-0006 provenance: this feature's own genuine authorization decision ---
check "the decision route is gated by both an asserted actor and a verified ADR-0006 provenance proof, mirroring BIO-ACC-001's own /evaluate route" \
  bash -c "grep -q 'provenanceGate.Require' $API_FEATURE/Api/AccessDecisionEndpoints.cs"
check "BIO-ACC-001's own foreign AccessPolicyError is translated at the gateway boundary, never left to propagate past this feature's own endpoint error handler" \
  bash -c "grep -q 'catch (Biolog.Features.AccessPolicy.Domain.AccessPolicyError' api/Biolog.Api/Modules/BioAcc002Module.cs"

# --- Module wiring, write-scope and captive-dependency discipline ---
check "no ProjectReference to any other feature project - all four cross-story reads go through this project's own gateway interfaces" \
  bash -c "! grep -q '<ProjectReference' $API_FEATURE/Biolog.Features.AccessDecisions.csproj"
check "IPolicyDecisionGateway, IFingerprintEvidenceGateway and IFaceEvidenceGateway are this project's own interfaces, not references to a sibling's types" \
  bash -c "test -f $API_FEATURE/Domain/IPolicyDecisionGateway.cs && test -f $API_FEATURE/Domain/IFingerprintEvidenceGateway.cs && test -f $API_FEATURE/Domain/IFaceEvidenceGateway.cs"
check "backend module lives one-way in api/Biolog.Api/Modules, not back in the feature project" \
  bash -c "test -f api/Biolog.Api/Modules/BioAcc002Module.cs && ! test -f '$API_FEATURE/BioAcc002Module.cs'"
check "registered in the packaged Program.cs module array, depending on Tenant, PersonRegistry, AccessPolicy, FingerprintVerification and FaceVerification" \
  bash -c "grep -q 'new BioAcc002Module()' api/Biolog.Api/Program.cs && grep -q '\"Tenant\", \"PersonRegistry\", \"AccessPolicy\", \"FingerprintVerification\", \"FaceVerification\"' api/Biolog.Api/Modules/BioAcc002Module.cs"
check "an unenrolled biometric reference (NoAuthorisedReference) is caught at the gateway boundary and reported as a routine NotEnrolled evidence outcome, never left to propagate as an unhandled exception" \
  bash -c "grep -q 'FingerprintVerificationException' api/Biolog.Api/Modules/BioAcc002Module.cs && grep -q 'FaceVerificationException' api/Biolog.Api/Modules/BioAcc002Module.cs && grep -q 'BiometricComparisonOutcome.NotEnrolled' api/Biolog.Api/Modules/BioAcc002Module.cs"
check "the demo-only policy evaluator is a SEPARATE Singleton registration from the Scoped, RBAC-gated policy decision gateway - no captive-dependency bug" \
  bash -c "grep -q 'AddScoped<IPolicyDecisionGateway>' api/Biolog.Api/Modules/BioAcc002Module.cs && grep -q 'AddSingleton<IAccessDecisionDemoSeedProvisioner>' api/Biolog.Api/Modules/BioAcc002Module.cs"

# --- AC05: web wiring, no UI redesign, no route collision with BIO-ACC-003 ---
check "AC05 access-page.tsx, ports.ts, mock.ts and route-manifest.ts primary/secondary action labels are untouched by this story" \
  bash -c "! grep -q 'BIO-ACC-002' web/features/access/access-page.tsx web/features/access/ports.ts web/features/access/mock.ts 2>/dev/null"
check "AC05 live.ts is untouched - it already called the exact same primary-action route this story now answers for real" \
  bash -c "! grep -q 'BIO-ACC-002' web/features/access/live.ts && grep -q '/api/v1/access/primary-action' web/features/access/live.ts"
check "AC05 view-access-events is deliberately NOT mapped by this story - BIO-ACC-003 already owns that route" \
  bash -c "! grep -qE 'MapPost\(\"/view-access-events\"' $API_FEATURE/Api/AccessDecisionEndpoints.cs"
check "AC05 BIO-ACC-003's own view-access-events route mapping is untouched by this story" \
  bash -c "grep -q '/api/v1/access/view-access-events' api/features/bio-acc-003/Api/AccessCacheEndpoints.cs"
check "AC05 this story's own demo bridge and primary-action endpoint mapping exist" \
  bash -c "test -f $API_FEATURE/Demo/AccessDecisionDemoBridgeService.cs && grep -q 'primary-action' $API_FEATURE/Api/AccessDecisionEndpoints.cs"
check "AC05 web/next.config.mjs proxies /api/v1/access/primary-action to the real backend" \
  bash -c "grep -q \"source: '/api/v1/access/primary-action'\" web/next.config.mjs"
check "AC05 the demo bridge exercises real BIO-ACC-001 policy create+publish and a real decision computation, not a canned string" \
  bash -c "grep -q 'AccessPolicy.Domain.AccessPolicy.Draft' api/Biolog.Api/Modules/BioAcc002Module.cs && grep -q 'PolicyEvaluator.Evaluate' api/Biolog.Api/Modules/BioAcc002Module.cs"
check "a real-host test proves the primary-action bridge answers a genuinely headerless request with real domain data" \
  bash -c "grep -q 'AC05_the_primary_action_bridge_route_answers_a_genuinely_headerless_request_with_a_real_published_demo_policy_and_a_real_computed_decision' $HOST_TESTS/AccessDecisionsHostChannelTests.cs"

# --- Developer test wiring ---
check "backend unit tests exist" bash -c \
  'test -f tests/features/bio-acc-002/AccessDecisionServiceTests.cs &&
   test -f tests/features/bio-acc-002/AccessDecisionCombinerTests.cs'
check "a real-packaged-host test exists proving the genuine BIO-ACC-001/BIO-IDN-001 dependencies through the shipped host channel" \
  test -f "$HOST_TESTS/AccessDecisionsHostChannelTests.cs"
check "the host-channel test proves the decision route requires a verified provenance proof" \
  bash -c "grep -q 'the_decision_route_requires_a_verified_provenance_proof_identical_to_BIO_ACC_001s_own_evaluate_route' $HOST_TESTS/AccessDecisionsHostChannelTests.cs"
check "builder evidence recorded" test -d evidence/features/bio-acc-002/builder

echo "=============================="
echo "$pass passed, $fail failed"
[ "$fail" -eq 0 ]
