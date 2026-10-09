#!/usr/bin/env bash
# Offline-safe structural checks for BIO-DOC-009 (automatic authenticity assessment
# orchestrator). Behavioural assertions (policy decisions, audit, override, versions) live in the
# executable xunit suite under
# tests/features/bio-doc-009/Biolog.Features.AuthenticityAssessment.Tests (see
# docs/features/bio-doc-009/AUTHENTICITY-ORCHESTRATOR.md, docs/TESTING.md).
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"
pass=0; fail=0
check() { local label="$1"; shift; if "$@"; then echo "PASS  $label"; pass=$((pass+1)); else echo "FAIL  $label"; fail=$((fail+1)); fi; }

FEATURE=api/features/bio-doc-009/Biolog.Features.AuthenticityAssessment
MODEL="$FEATURE/Model"
AUDIT="$FEATURE/Audit"
APID="$FEATURE/Api"
TESTS=tests/features/bio-doc-009/Biolog.Features.AuthenticityAssessment.Tests

# --- AC01: assessment reports individual checks and overall policy result ---
check "AC01 AuthenticityCheckId names the six individual checks" test -f "$MODEL/AuthenticityCheckId.cs"
check "AC01 AuthenticityDisposition is the overall policy result" test -f "$MODEL/AuthenticityDisposition.cs"
check "AC01 AuthenticityAssessmentResult carries both Checks and PolicyDisposition" \
  bash -c "grep -q 'IReadOnlyList<AuthenticityCheckResult> Checks' '$MODEL/AuthenticityAssessmentResult.cs' && grep -q 'AuthenticityDisposition PolicyDisposition' '$MODEL/AuthenticityAssessmentResult.cs'"
check "AC01 test coverage exists" \
  bash -c "grep -q 'AC01_' '$TESTS/AuthenticityPolicyEngineTests.cs'"

# --- AC02: barcode decode alone can never produce DOCUMENT AUTHENTIC ---
check "AC02 DetermineDisposition never reads BarcodeDecode's own outcome" \
  bash -c "! sed -n '/public static AuthenticityDisposition DetermineDisposition/,/^    }/p' '$MODEL/AuthenticityPolicyEngine.cs' | grep -q 'AuthenticityCheckId.BarcodeDecode'"
check "AC02 promotion to DocumentAuthentic reads only CrossSourceFieldComparison and PassiveAuthentication" \
  bash -c "grep -q 'AuthenticityCheckId.CrossSourceFieldComparison' '$MODEL/AuthenticityPolicyEngine.cs' && grep -q 'AuthenticityCheckId.PassiveAuthentication' '$MODEL/AuthenticityPolicyEngine.cs'"
check "AC02 fewer than two sources never reaches Passed on cross-source comparison (BIO-DOC-008's own rule is respected, not re-decided)" \
  bash -c "grep -q 'matchedCount == 0' '$MODEL/AuthenticityPolicyEngine.cs'"
check "AC02 test coverage exists, including a direct proof BarcodeDecode's outcome is never read" \
  bash -c "grep -q 'AC02_barcode_decode_passed_with_every_other_check_not_performed_is_never_document_authentic' '$TESTS/AuthenticityPolicyEngineTests.cs' \
    && grep -q 'AC02_DetermineDisposition_never_reads_the_BarcodeDecode_outcome_to_decide_authenticity' '$TESTS/AuthenticityPolicyEngineTests.cs'"

# --- AC03: unsupported checks are marked not performed, not passed ---
check "AC03 AuthenticityCheckOutcome has a distinct NotPerformed value, ordered first (the safe default)" \
  bash -c "python3 - <<'PY'
from pathlib import Path
text = Path('$MODEL/AuthenticityCheckOutcome.cs').read_text()
body = text.split('enum AuthenticityCheckOutcome', 1)[1]
first_member = body.split('{', 1)[1].strip().split(',', 1)[0].strip()
assert first_member == 'NotPerformed', f'expected NotPerformed to be the first enum member, got {first_member!r}'
PY"
check "AC03 every input outcome field defaults to NotPerformed, never Passed" \
  bash -c "grep -c 'AuthenticityCheckOutcome.NotPerformed;' '$MODEL/AuthenticityAssessmentInputs.cs' | grep -qE '^[3-9]'"
check "AC03 a dedicated NotPerformed factory exists so every 'not performed' report is constructed the same way" \
  bash -c "grep -q 'static AuthenticityCheckResult NotPerformed' '$MODEL/AuthenticityCheckResult.cs'"
check "AC03 test coverage exists" \
  bash -c "grep -q 'AC03_' '$TESTS/AuthenticityPolicyEngineTests.cs'"

# --- AC04: evidence, template and ruleset versions are recorded ---
check "AC04 AuthenticityAssessmentResult carries EvidenceVersion, TemplateId/TemplateVersionNumber, RulesetVersion" \
  bash -c "grep -q 'int EvidenceVersion' '$MODEL/AuthenticityAssessmentResult.cs' \
    && grep -q 'string? TemplateId' '$MODEL/AuthenticityAssessmentResult.cs' \
    && grep -q 'int? TemplateVersionNumber' '$MODEL/AuthenticityAssessmentResult.cs' \
    && grep -q 'string RulesetVersion' '$MODEL/AuthenticityAssessmentResult.cs'"
check "AC04 EvidenceVersion is BIO-DOC-008's own already-controlled CrossComparisonResult.VersionNumber, not a second counter" \
  bash -c "grep -q 'inputs.CrossComparison.VersionNumber' '$MODEL/AuthenticityAssessmentResult.cs'"
check "AC04 RulesetVersion is recorded from AuthenticityPolicyEngine's own named constant" \
  bash -c "grep -q 'AuthenticityPolicyEngine.RulesetVersion' '$MODEL/AuthenticityAssessmentResult.cs'"
check "AC04 test coverage exists" \
  bash -c "grep -q 'AC04_' '$TESTS/AuthenticityAssessmentResultTests.cs'"

# --- Compute is durably audited before returning, the same discipline as BIO-DOC-008's own Compute ---
check "a real audit-sink pair exists (IAuthenticityAssessmentAuditSink + InMemory implementation)" \
  bash -c "test -f '$AUDIT/IAuthenticityAssessmentAuditSink.cs' && test -f '$AUDIT/InMemoryAuthenticityAssessmentAuditSink.cs'"
check "Compute durably writes an audit event before returning, never after" \
  bash -c "grep -q 'WriteAuditEventOrThrow' '$MODEL/AuthenticityAssessmentResult.cs'"
check "a genuinely unavailable audit sink throws rather than returning a successful result" \
  bash -c "grep -q 'AuthenticityAssessmentAuditUnavailableException' '$MODEL/AuthenticityAssessmentResult.cs'"
check "an ambiguous write is resolved via ExistsAsync before treating the sink as unavailable" \
  bash -c "grep -q 'ExistsAsync' '$MODEL/AuthenticityAssessmentResult.cs'"
check "computedAt is validated as a strict RFC 3339 timestamp, not accepted as an arbitrary string" \
  bash -c "grep -q 'Rfc3339Pattern' '$MODEL/AuthenticityAssessmentResult.cs'"

# --- AC05: manual review can override workflow disposition but cannot rewrite raw evidence ---
check "AC05 AuthenticityManualOverride exists as a layer ON TOP of the result, not a mutation of it" \
  test -f "$MODEL/AuthenticityManualOverride.cs"
check "AC05 the mutable aggregate exists (AuthenticityAssessment), CAS-versioned" \
  bash -c "grep -q 'int Version' '$MODEL/AuthenticityAssessment.cs' && grep -q 'AuthenticityAssessmentConcurrencyException' '$MODEL/AuthenticityAssessment.cs'"
check "AC05 ApplyManualOverride never assigns to Result (structural proof: no 'Result =' assignment inside it)" \
  bash -c "python3 - <<'PY'
from pathlib import Path
text = Path('$MODEL/AuthenticityAssessment.cs').read_text()
method = text.split('public void ApplyManualOverride(', 1)[1]
body = method.split('lock (_gate)', 1)[1]
# Only the closing brace of ApplyManualOverride should follow - crudely bound the scan to a
# reasonable window so a later method's own code is never included by accident.
window = body[:2000]
assert 'Result =' not in window, 'ApplyManualOverride must never assign to Result'
assert 'Override = new AuthenticityManualOverride' in window, 'expected ApplyManualOverride to assign Override'
PY"
check "AC05 an override is durably audited before returning" \
  bash -c "grep -q 'authenticity_assessment.manually_overridden' '$AUDIT/AuthenticityAssessmentAuditEventBuilder.cs'"
check "AC05 EffectiveDisposition falls back to the policy result when no override is active" \
  bash -c "grep -q 'Override?.Disposition ?? Result.PolicyDisposition' '$MODEL/AuthenticityAssessment.cs'"
check "AC05 test coverage exists, including a same-reference proof the raw checks are untouched" \
  bash -c "grep -q 'AC05_a_manual_override_never_changes_the_individual_checks_the_raw_evidence' tests/features/bio-doc-009/Biolog.Features.AuthenticityAssessment.Tests/AuthenticityAssessmentTests.cs \
    && grep -q 'Assert.Same(originalChecks, assessment.Result.Checks)' tests/features/bio-doc-009/Biolog.Features.AuthenticityAssessment.Tests/AuthenticityAssessmentTests.cs"

# --- AC06: connected through DocumentVerifyService to a real live implementation ---
check "AC06 a real HTTP endpoint exists at POST /api/v1/documents/authenticity" \
  bash -c "grep -q \"MapPost(BasePath, ComputeAsync)\" '$APID/AuthenticityEndpoints.cs' && grep -q '/api/v1/documents/authenticity' '$APID/AuthenticityEndpoints.cs'"
check "AC06 the module is registered into the host (Program.cs, one line, additive)" \
  bash -c "grep -q 'new BioDoc009Module()' api/Biolog.Api/Program.cs"
check "AC06 the module adapter lives in api/Biolog.Api/Modules, not inside the feature project" \
  test -f api/Biolog.Api/Modules/BioDoc009Module.cs
check "AC06 web/features/documents already targets this exact route (BIO-UX-009, unmodified contract)" \
  bash -c "grep -q \"authenticity: '/api/v1/documents/authenticity'\" web/features/documents/live.ts"
check "AC06 DocumentVerifyService gains an independent 'authenticity' port key (genuine HYBRID, not all-or-nothing; BIO-DOC-010 later adds a third, documentIdentification - see tests/foundation/BIO-DOC-010.test.sh for that check)" \
  bash -c "grep -qE \"DocumentVerifyService: \['primaryAction', 'authenticity'(,|\])\" web/lib/feature-mode.ts"
check "AC06 the Next.js rewrite forwards this story's own route family to the API host, scoped narrowly" \
  bash -c "grep -q \"'/api/v1/documents/:path\*'\" web/next.config.mjs"
check "AC06 no unapproved UI redesign - DocumentVerifyApp.tsx is untouched by this story" \
  bash -c "! git diff --name-only HEAD -- web/features/documents/DocumentVerifyApp.tsx web/features/documents/document-verify.module.css 2>/dev/null | grep -q . \
    && ! git diff --cached --name-only HEAD -- web/features/documents/DocumentVerifyApp.tsx web/features/documents/document-verify.module.css 2>/dev/null | grep -q ."
check "AC06 frontend test coverage exists for the new independent port key" \
  test -f tests/frontend/unit/bio-doc-009-documents-authenticity-port.test.ts
check "AC06 a Playwright test exercises the real HTTP endpoint end to end" \
  test -f tests/frontend/playwright/bio-doc-009-authenticity-live.spec.ts

# --- Zainab's security review (BIO-DOC-009-F01, High): unauthenticated GET-by-id and override
# routes were removed entirely, not just left mapped without a fix ---
check "F01 GET /api/v1/documents/authenticity/{id} is not mapped (no session-id-driven read surface)" \
  bash -c "! grep -q 'MapGet' '$APID/AuthenticityEndpoints.cs'"
check "F01 POST .../{id}/override is not mapped (no caller-supplied-identity override surface)" \
  bash -c "! grep -q 'OverrideAsync' '$APID/AuthenticityEndpoints.cs'"
check "F01 Map() registers exactly one route (the compute POST) on this endpoint class" \
  bash -c "test \"\$(grep -c 'endpoints\.Map' '$APID/AuthenticityEndpoints.cs')\" -eq 1"
check "F01 the service-layer override/read capability is kept as a seam, not deleted outright" \
  bash -c "grep -q 'public Model.AuthenticityAssessment? Get' '$APID/AuthenticityAssessmentService.cs' \
    && grep -q 'public Model.AuthenticityAssessment ApplyOverride' '$APID/AuthenticityAssessmentService.cs'"
check "F01 the kept service-layer seam has its own direct, non-HTTP test coverage" \
  test -f tests/features/bio-doc-009/Biolog.Features.AuthenticityAssessment.Tests/AuthenticityAssessmentServiceTests.cs
check "F01 the demo fixtures no longer own a shared static version store/audit sink (the bug that coverage found)" \
  bash -c "! grep -qE 'private static readonly I(VersionStore|EvidenceComparisonAuditSink)' '$FEATURE/Demo/SyntheticAuthenticityFixtures.cs'"

# --- QA-03-lesson-carried-forward: locked-down result types, applied proactively ---
check "AuthenticityCheckResult/AuthenticityAssessmentResult/AuthenticityManualOverride properties are internal init, not public init" \
  bash -c "! grep -qE '\{ get; init; \}' '$MODEL/AuthenticityCheckResult.cs' \
    && ! grep -qE '\{ get; init; \}' '$MODEL/AuthenticityAssessmentResult.cs' \
    && ! grep -qE '\{ get; init; \}' '$MODEL/AuthenticityManualOverride.cs'"
check "AuthenticityAssessmentResult's list property uses AsReadOnly(), not a downcastable interface view" \
  bash -c "grep -q 'AsReadOnly()' '$MODEL/AuthenticityAssessmentResult.cs'"
check "standing reflection-based lockdown regression test exists" \
  test -f tests/features/bio-doc-009/Biolog.Features.AuthenticityAssessment.Tests/AuthenticityAssessmentResultTypesAreLockedDownTests.cs

# --- Dependency direction: this feature depends on BIO-DOC-006/008 only ---
check "AuthenticityAssessment references BIO-DOC-006's document classifier" \
  bash -c "grep -q 'Biolog.Features.DocumentClassifier.csproj' '$FEATURE/Biolog.Features.AuthenticityAssessment.csproj'"
check "AuthenticityAssessment references BIO-DOC-008's evidence comparison engine" \
  bash -c "grep -q 'Biolog.Features.EvidenceComparison.csproj' '$FEATURE/Biolog.Features.AuthenticityAssessment.csproj'"
check "AuthenticityAssessment does NOT reference BIO-DOC-001/002/003/004/005/007 directly (not declared dependencies)" \
  bash -c "! grep -qE 'bio-doc-00[12345 7]/' '$FEATURE/Biolog.Features.AuthenticityAssessment.csproj'"
check "no sibling BIO-DOC-00x project takes a dependency back on this feature (no cycle)" \
  bash -c "! grep -rq 'bio-doc-009' api/features/bio-doc-006/*/*.csproj api/features/bio-doc-008/*/*.csproj"
check "this feature project never references Biolog.Api.csproj (one-way dependency)" \
  bash -c "! grep -qE '<ProjectReference[^>]*Biolog\.Api\.csproj' '$FEATURE/Biolog.Features.AuthenticityAssessment.csproj'"

# --- No new canonical identifier prefix invented locally (ADR-0001 S1.2); shared contracts untouched ---
check "ADR-0001-S1.2 the frozen identifier registry was not touched by this story" \
  bash -c "! git diff --cached --name-only HEAD -- contracts/ 2>/dev/null | grep -q . && ! git diff --name-only HEAD -- contracts/ 2>/dev/null | grep -q ."

# --- Test/evidence/traceability scaffolding exists ---
check "feature test project exists" test -f "$TESTS/Biolog.Features.AuthenticityAssessment.Tests.csproj"
check "story traceability spec exists" test -f governance/stories/BIO-DOC-009.json
check "feature design doc exists" test -f docs/features/bio-doc-009/AUTHENTICITY-ORCHESTRATOR.md

echo "----"
echo "BIO-DOC-009 structural checks: $pass passed, $fail failed"
[ "$fail" -eq 0 ]
