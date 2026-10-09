#!/usr/bin/env bash
# Offline-safe structural checks for BIO-DOC-005 (barcode parser registry and tenant parser
# workflow). Behavioural assertions (authorship, versioning/workflow, isolation, execution) live
# in the executable xunit suite under tests/features/bio-doc-005/Biolog.Features.ParserRegistry.Tests
# (see docs/features/bio-doc-005/PARSER-REGISTRY.md, docs/TESTING.md).
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"
pass=0; fail=0
check() { local label="$1"; shift; if "$@"; then echo "PASS  $label"; pass=$((pass+1)); else echo "FAIL  $label"; fail=$((fail+1)); fi; }

FEATURE=api/features/bio-doc-005/Biolog.Features.ParserRegistry
MODEL="$FEATURE/Model"

# --- AC01: BTS and permitted tenants can define parsers ---
check "AC01 ParserCaller (Bts / Tenant) exists" test -f "$MODEL/ParserCaller.cs"
check "AC01 TenantParserAuthorityRegistry gates tenant authorship" test -f "$MODEL/TenantParserAuthorityRegistry.cs"
check "AC01 ParserCatalog enforces who may author before drafting" \
  bash -c "grep -q 'RequireAuthorityToAuthor' '$MODEL/ParserCatalog.cs'"
check "AC01 only BTS may author a Global-scoped parser" \
  bash -c "grep -q 'only_bts_may_author_global_parsers' '$MODEL/ParserCatalog.cs'"
check "AC01 an unauthorized tenant is rejected, not silently allowed" \
  bash -c "grep -q 'tenant_not_authorized_to_author_parsers' '$MODEL/ParserCatalog.cs'"

# --- AC02: parser definitions are versioned, tested and published through approval ---
check "AC02 ParserStatus is Draft/Tested/Published" \
  bash -c "grep -q 'Draft' '$MODEL/ParserStatus.cs' && grep -q 'Tested' '$MODEL/ParserStatus.cs' && grep -q 'Published' '$MODEL/ParserStatus.cs'"
check "AC02 publishing requires the version to have been tested first" \
  bash -c "grep -q 'parser_version_not_tested' '$MODEL/ParserRecord.cs'"
check "AC02 publishing is maker-checker - no self-approval" \
  bash -c "grep -q 'self_approval_not_permitted' '$MODEL/ParserRecord.cs'"
check "AC02 the aggregate follows ADR-0001 Decision 3 optimistic concurrency" \
  bash -c "grep -q 'ConcurrencyVersion' '$MODEL/ParserRecord.cs' && grep -q 'class ConcurrencyException' '$MODEL/ParserRecord.cs'"

# --- AC03: a parser specifies document/issuer applicability, field mapping and validation ---
check "AC03 ParserApplicability model exists" test -f "$MODEL/ParserApplicability.cs"
check "AC03 ParserFieldMapping and ParserValidationRule are distinct types" \
  bash -c "grep -q 'record ParserFieldMapping' '$MODEL/ParserFieldMapping.cs' && grep -q 'record ParserValidationRule' '$MODEL/ParserFieldMapping.cs'"
check "AC03 ParserExecutor applies field mapping and validation to a raw payload" \
  test -f "$MODEL/ParserExecutor.cs"
check "AC03 execution only ever runs against a Published version" \
  bash -c "grep -q 'only_a_published_parser_version_may_be_applied' '$MODEL/ParserExecutor.cs'"

# --- Round 1 review finding (BIO-DOC-005-F01, High): tenant-authored validation regex ReDoS ---
check "F01 pattern matching uses a bounded timeout, not unbounded Regex.IsMatch" \
  bash -c "grep -q 'MatchTimeout' '$MODEL/ParserExecutor.cs' && grep -qE 'Regex\.IsMatch\([^)]*MatchTimeout' '$MODEL/ParserExecutor.cs'"
check "F01 a regex timeout is caught and reported honestly, never left to crash the caller" \
  bash -c "grep -q 'RegexMatchTimeoutException' '$MODEL/ParserExecutor.cs' && grep -q 'pattern_timeout' '$MODEL/ParserExecutor.cs'"

# --- AC04: published parser changes create a new version ---
check "AC04 revising a non-Draft version starts a new version rather than mutating it" \
  bash -c "grep -q 'VersionNumber + 1' '$MODEL/ParserRecord.cs'"

# --- AC05: tenant parser cannot inspect another tenant ---
check "AC05 ParserNotFoundException exists (fail-closed, ADR-0002 S4.3 pattern)" \
  test -f "$MODEL/ParserNotFoundException.cs"
check "AC05 ParserOwnerScope.IsVisibleTo is the single visibility gate" \
  bash -c "grep -q 'IsVisibleTo' '$MODEL/ParserOwnerScope.cs' && grep -q 'IsVisibleTo' '$MODEL/ParserCatalog.cs'"
check "AC05 Get is called before every mutating operation, not just reads" \
  bash -c "grep -c 'var record = Get(caller, parserId);' '$MODEL/ParserCatalog.cs' | grep -q '^3$'"

# --- QA-03 lesson carried forward: trust-bearing result types cannot be built or copy-modified externally ---
check "ParserVersionContent properties are internal init, not public init" \
  bash -c "! grep -qE '\{ get; init; \}' '$MODEL/ParserVersionContent.cs'"
check "ParserExecutionResult/ParserFieldResult properties are internal init, not public init" \
  bash -c "! grep -qE '\{ get; init; \}' '$MODEL/ParserExecutionResult.cs'"
check "standing reflection-based lockdown regression test exists" \
  test -f tests/features/bio-doc-005/Biolog.Features.ParserRegistry.Tests/ParserResultTypesAreLockedDownTests.cs

# --- Dependency direction: this feature depends on BIO-DOC-003 only, never the reverse ---
check "ParserRegistry references BIO-DOC-003's barcode evidence model" \
  bash -c "grep -q 'Biolog.Features.BarcodeEngine.csproj' '$FEATURE/Biolog.Features.ParserRegistry.csproj'"
check "BIO-DOC-003's own project takes no dependency back on this feature (no cycle)" \
  bash -c "! grep -q 'bio-doc-005' api/features/bio-doc-003/Biolog.Features.BarcodeEngine/Biolog.Features.BarcodeEngine.csproj"
check "no ASP.NET Core / Biolog.Api project or framework reference was added to this feature" \
  bash -c "! grep -qE '<(ProjectReference|FrameworkReference)[^>]*(Biolog\.Api|Microsoft\.AspNetCore)' '$FEATURE/Biolog.Features.ParserRegistry.csproj'"

# --- No new canonical identifier prefix invented locally (ADR-0001 S1.2); shared contracts untouched ---
check "ADR-0001-S1.2 the frozen identifier registry was not touched by this story" \
  bash -c "! git diff --cached --name-only HEAD -- contracts/ 2>/dev/null | grep -q . && ! git diff --name-only HEAD -- contracts/ 2>/dev/null | grep -q ."

# --- Test/evidence/traceability scaffolding exists ---
check "feature test project exists" test -f tests/features/bio-doc-005/Biolog.Features.ParserRegistry.Tests/Biolog.Features.ParserRegistry.Tests.csproj
check "story traceability spec exists" test -f governance/stories/BIO-DOC-005.json
check "feature design doc exists" test -f docs/features/bio-doc-005/PARSER-REGISTRY.md

echo "----"
echo "BIO-DOC-005 structural checks: $pass passed, $fail failed"
[ "$fail" -eq 0 ]
