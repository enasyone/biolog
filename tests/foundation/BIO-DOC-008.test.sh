#!/usr/bin/env bash
# Offline-safe structural checks for BIO-DOC-008 (evidence cross-comparison engine). Behavioural
# assertions (comparison outcomes, precedence resolution, review routing, NFC corroboration) live
# in the executable xunit suite under tests/features/bio-doc-008/Biolog.Features.EvidenceComparison.Tests
# (see docs/features/bio-doc-008/EVIDENCE-COMPARISON.md, docs/TESTING.md).
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"
pass=0; fail=0
check() { local label="$1"; shift; if "$@"; then echo "PASS  $label"; pass=$((pass+1)); else echo "FAIL  $label"; fail=$((fail+1)); fi; }

FEATURE=api/features/bio-doc-008/Biolog.Features.EvidenceComparison
MODEL="$FEATURE/Model"

# --- AC01: comparable fields show source-by-source values and match/mismatch/unknown ---
check "AC01 EvidenceSourceKind names visual/MRZ/barcode/NFC" test -f "$MODEL/EvidenceSourceKind.cs"
check "AC01 FieldMatchStatus is exactly match/mismatch/unknown" \
  bash -c "grep -q 'Match' '$MODEL/FieldMatchStatus.cs' && grep -q 'Mismatch' '$MODEL/FieldMatchStatus.cs' && grep -q 'Unknown' '$MODEL/FieldMatchStatus.cs'"
check "AC01 fewer than two sources is Unknown, never a trivial match" \
  bash -c "grep -q 'valuesBySource.Count < 2' '$MODEL/ComparisonEngine.cs'"
check "AC01 ComparisonEngine is the only place Status is derived" test -f "$MODEL/ComparisonEngine.cs"

# --- AC02: normalization never destroys raw evidence ---
check "AC02 SourceFieldValue requires both raw and normalized values together" \
  bash -c "grep -q 'string RawValue' '$MODEL/SourceFieldValue.cs' && grep -q 'string NormalizedValue' '$MODEL/SourceFieldValue.cs'"
check "AC02 Create rejects an empty normalized value" \
  bash -c "grep -q 'normalized value must not be empty' '$MODEL/SourceFieldValue.cs'"
check "AC02 the barcode adapter preserves RawValue even when validation failed" \
  bash -c "grep -q 'FromParsedBarcodeField' '$MODEL/SourceFieldValue.cs'"

# --- AC03: conflicts route to review ---
check "AC03 FieldsRequiringReview names mismatched fields explicitly" \
  bash -c "grep -q 'FieldsRequiringReview' '$MODEL/CrossComparisonResult.cs' && grep -q 'FieldMatchStatus.Mismatch' '$MODEL/CrossComparisonResult.cs'"
check "AC03 a failed NFC passive authentication also forces review, not just field mismatches" \
  bash -c "grep -q 'PassiveAuthenticationPassed == false' '$MODEL/CrossComparisonResult.cs'"

# --- AC04: no source is silently preferred without documented policy ---
check "AC04 SourcePrecedencePolicy exists as an explicit, caller-declared order" test -f "$MODEL/SourcePrecedencePolicy.cs"
check "AC04 PreferredValueResolver requires the policy - no default/implicit precedence" \
  bash -c "grep -q 'SourcePrecedencePolicy policy' '$MODEL/PreferredValueResolver.cs'"
check "AC04 a policy rejects duplicate sources" \
  bash -c "grep -q 'must not repeat a source' '$MODEL/SourcePrecedencePolicy.cs'"
check "AC04 no other engine method resolves a value without a precedence policy (structural test exists)" \
  test -f tests/features/bio-doc-008/Biolog.Features.EvidenceComparison.Tests/PreferredValueResolverTests.cs

# --- AC05: comparison result is versioned/audited ---
check "AC05 CrossComparisonResult carries a version number" \
  bash -c "grep -q 'int VersionNumber' '$MODEL/CrossComparisonResult.cs'"
check "AC05 CrossComparisonResult carries audit attribution (actor + timestamp)" \
  bash -c "grep -q 'ComputedByActorId' '$MODEL/CrossComparisonResult.cs' && grep -q 'ComputedAt' '$MODEL/CrossComparisonResult.cs'"

# --- Acceptance finding (BIO-DOC-008-QA-01, High): version/audit were unvalidated attribution only, no durable audit write ---
AUDIT="$FEATURE/Audit"
check "QA-01 a real audit-sink pair exists (IEvidenceComparisonAuditSink + InMemory implementation)" \
  bash -c "test -f '$AUDIT/IEvidenceComparisonAuditSink.cs' && test -f '$AUDIT/InMemoryEvidenceComparisonAuditSink.cs'"
check "QA-01 Compute durably writes an audit event before returning, never after" \
  bash -c "grep -q 'WriteAuditEventOrThrow' '$MODEL/CrossComparisonResult.cs'"
check "QA-01 a genuinely unavailable audit sink throws rather than returning a successful comparison" \
  bash -c "grep -q 'EvidenceComparisonAuditUnavailableException' '$MODEL/CrossComparisonResult.cs'"
check "QA-01 an ambiguous write is resolved via ExistsAsync before treating the sink as unavailable" \
  bash -c "grep -q 'ExistsAsync' '$MODEL/CrossComparisonResult.cs'"
check "QA-01 versionNumber below 1 is rejected" \
  bash -c "grep -q 'versionNumber < 1' '$MODEL/CrossComparisonResult.cs'"
check "QA-01 a blank actor id is rejected" \
  bash -c "grep -q 'IsNullOrWhiteSpace(computedByActorId)' '$MODEL/CrossComparisonResult.cs'"
check "QA-01 computedAt is validated as a real timestamp, not accepted as an arbitrary string" \
  bash -c "grep -q 'DateTimeOffset.TryParse(computedAt' '$MODEL/CrossComparisonResult.cs'"

# --- Independent re-review finding (BIO-DOC-008-QA-02, High): timestamp validation was not RFC 3339 strict ---
check "QA-02 a strict RFC 3339 shape pattern is required before TryParse is ever consulted" \
  bash -c "grep -q 'Rfc3339Pattern' '$MODEL/CrossComparisonResult.cs'"
check "QA-02 the pattern requires an explicit Z or numeric offset, not a bare date" \
  bash -c "grep -qE '\(Z\|\[\+-\]' '$MODEL/CrossComparisonResult.cs'"

# --- Second acceptance re-review finding (BIO-DOC-008-QA-03, High): version was a bare range
# check, not controlled sequencing - a caller could reuse, skip, or reverse version numbers ---
check "QA-03 IVersionStore exists as the sole path to a legitimate version claim" \
  test -f "$MODEL/IVersionStore.cs"
check "QA-03 IVersionStore.AllocateVersion is the atomic check-and-commit operation" \
  bash -c "grep -q 'AllocateVersion' '$MODEL/IVersionStore.cs'"
check "QA-03 an in-memory version store implementation exists" \
  test -f "$MODEL/InMemoryVersionStore.cs"
check "QA-03 Compute takes an IVersionStore and calls AllocateVersion before returning a result" \
  bash -c "grep -q 'IVersionStore versionStore' '$MODEL/CrossComparisonResult.cs' && grep -q 'versionStore.AllocateVersion' '$MODEL/CrossComparisonResult.cs'"
check "QA-03 a rejected version claim throws a dedicated StaleComparisonVersionException" \
  bash -c "grep -q 'StaleComparisonVersionException' '$MODEL/CrossComparisonResult.cs'"
check "QA-03 regressions exist for duplicate, skipped, and backward versions" \
  bash -c "grep -q 'reusing_a_version_number_for_the_same_session_is_rejected' tests/features/bio-doc-008/Biolog.Features.EvidenceComparison.Tests/CrossComparisonResultTests.cs \
    && grep -q 'skipping_ahead_to_an_arbitrary_version_for_the_same_session_is_rejected' tests/features/bio-doc-008/Biolog.Features.EvidenceComparison.Tests/CrossComparisonResultTests.cs \
    && grep -q 'moving_backwards_for_the_same_session_is_rejected' tests/features/bio-doc-008/Biolog.Features.EvidenceComparison.Tests/CrossComparisonResultTests.cs"

# --- Second acceptance FAIL (BIO-DOC-008-QA-04, High): AllocateVersion committed the version
# BEFORE the mandatory audit write, so a failed audit permanently burned the version - fixed with
# a genuine two-phase reserve/commit/release protocol ---
check "QA-04 IVersionStore.CommitVersion finalizes a reservation only after it is durable" \
  bash -c "grep -q 'CommitVersion' '$MODEL/IVersionStore.cs'"
check "QA-04 IVersionStore.ReleaseVersion discards a reservation without consuming it" \
  bash -c "grep -q 'ReleaseVersion' '$MODEL/IVersionStore.cs'"
check "QA-04 Compute allocates the version BEFORE the audit write and commits AFTER it" \
  bash -c "python3 - <<'PY'
from pathlib import Path
text = Path('$MODEL/CrossComparisonResult.cs').read_text()
compute = text.split('public static CrossComparisonResult Compute(', 1)[1]
allocate = compute.index('versionStore.AllocateVersion')
audit = compute.index('WriteAuditEventOrThrow')
commit = compute.index('versionStore.CommitVersion')
release = compute.index('versionStore.ReleaseVersion')
assert allocate < audit < commit, 'expected AllocateVersion before WriteAuditEventOrThrow before CommitVersion'
assert release > audit, 'expected ReleaseVersion to guard the audit write path'
PY"
check "QA-04 a failed audit write does not permanently burn the reserved version (regression)" \
  bash -c "grep -q 'a_failed_audit_write_does_not_permanently_burn_the_reserved_version' tests/features/bio-doc-008/Biolog.Features.EvidenceComparison.Tests/CrossComparisonResultTests.cs"
check "QA-04 an outstanding reservation blocks a second reservation for the same session (regression)" \
  bash -c "grep -q 'a_reservation_still_outstanding_for_a_session_blocks_a_second_reservation' tests/features/bio-doc-008/Biolog.Features.EvidenceComparison.Tests/CrossComparisonResultTests.cs"

# --- NFC corroboration, not fabricated field values (see design doc for why) ---
check "NfcCorroboration exists and is derived from a real BIO-DOC-007 ChipReadOutcome" \
  bash -c "grep -q 'ChipReadOutcome' '$MODEL/NfcCorroboration.cs'"
check "NfcCorroboration.PassiveAuthenticationPassed is nullable - never coerced when not attempted" \
  bash -c "grep -q 'bool? PassiveAuthenticationPassed' '$MODEL/NfcCorroboration.cs'"

# --- QA-03 lesson carried forward, applied proactively (BIO-DOC-007-QA-02/QA-03) ---
check "FieldComparison properties are internal init, not public init" \
  bash -c "! grep -qE '\{ get; init; \}' '$MODEL/FieldComparison.cs'"
check "CrossComparisonResult properties are internal init, not public init" \
  bash -c "! grep -qE '\{ get; init; \}' '$MODEL/CrossComparisonResult.cs'"
check "FieldComparison's ValuesBySource is a genuine ReadOnlyDictionary, not a downcastable interface view" \
  bash -c "grep -q 'ReadOnlyDictionary' '$MODEL/FieldComparison.cs'"
check "CrossComparisonResult's list properties use AsReadOnly(), not a downcastable interface view" \
  bash -c "grep -q 'AsReadOnly()' '$MODEL/CrossComparisonResult.cs'"
check "standing reflection-based lockdown regression test exists" \
  test -f tests/features/bio-doc-008/Biolog.Features.EvidenceComparison.Tests/EvidenceComparisonResultTypesAreLockedDownTests.cs

# --- Dependency direction: this feature depends on BIO-DOC-002/005/007 only ---
check "EvidenceComparison references BIO-DOC-002's MRZ engine" \
  bash -c "grep -q 'Biolog.Features.MrzEngine.csproj' '$FEATURE/Biolog.Features.EvidenceComparison.csproj'"
check "EvidenceComparison references BIO-DOC-005's parser registry" \
  bash -c "grep -q 'Biolog.Features.ParserRegistry.csproj' '$FEATURE/Biolog.Features.EvidenceComparison.csproj'"
check "EvidenceComparison references BIO-DOC-007's NFC reader" \
  bash -c "grep -q 'Biolog.Features.NfcReader.csproj' '$FEATURE/Biolog.Features.EvidenceComparison.csproj'"
check "EvidenceComparison does NOT reference BIO-DOC-001/003/004/006 directly (not declared dependencies)" \
  bash -c "! grep -qE 'bio-doc-00[1346]/' '$FEATURE/Biolog.Features.EvidenceComparison.csproj'"
check "no sibling BIO-DOC-00x project takes a dependency back on this feature (no cycle)" \
  bash -c "! grep -rq 'bio-doc-008' api/features/bio-doc-002/*/*.csproj api/features/bio-doc-005/*/*.csproj api/features/bio-doc-007/*/*.csproj"
check "no ASP.NET Core / Biolog.Api project or framework reference was added to this feature" \
  bash -c "! grep -qE '<(ProjectReference|FrameworkReference)[^>]*(Biolog\.Api|Microsoft\.AspNetCore)' '$FEATURE/Biolog.Features.EvidenceComparison.csproj'"

# --- No new canonical identifier prefix invented locally (ADR-0001 S1.2); shared contracts untouched ---
check "ADR-0001-S1.2 the frozen identifier registry was not touched by this story" \
  bash -c "! git diff --cached --name-only HEAD -- contracts/ 2>/dev/null | grep -q . && ! git diff --name-only HEAD -- contracts/ 2>/dev/null | grep -q ."

# --- Test/evidence/traceability scaffolding exists ---
check "feature test project exists" test -f tests/features/bio-doc-008/Biolog.Features.EvidenceComparison.Tests/Biolog.Features.EvidenceComparison.Tests.csproj
check "story traceability spec exists" test -f governance/stories/BIO-DOC-008.json
check "feature design doc exists" test -f docs/features/bio-doc-008/EVIDENCE-COMPARISON.md

echo "----"
echo "BIO-DOC-008 structural checks: $pass passed, $fail failed"
[ "$fail" -eq 0 ]
