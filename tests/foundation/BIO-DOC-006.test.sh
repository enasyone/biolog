#!/usr/bin/env bash
# Offline-safe structural checks for BIO-DOC-006 (automatic document type identification).
# Behavioural assertions (ranking/confidence, unknown outcome, side detection, versioned registry,
# operator confirmation) live in the executable xunit suite under
# tests/features/bio-doc-006/Biolog.Features.DocumentClassifier.Tests (see
# docs/features/bio-doc-006/DOCUMENT-CLASSIFIER.md, docs/TESTING.md).
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"
pass=0; fail=0
check() { local label="$1"; shift; if "$@"; then echo "PASS  $label"; pass=$((pass+1)); else echo "FAIL  $label"; fail=$((fail+1)); fi; }

FEATURE=api/features/bio-doc-006/Biolog.Features.DocumentClassifier
MODEL="$FEATURE/Model"

# --- AC01: classifier can return ranked candidates with confidence ---
check "AC01 DocumentClassCandidate model exists" test -f "$MODEL/DocumentClassCandidate.cs"
check "AC01 the classifier engine exists" test -f "$MODEL/Classifier.cs"
check "AC01 candidates are ranked by confidence, best first" \
  bash -c "grep -q 'OrderByDescending(c => c.Confidence)' '$MODEL/Classifier.cs'"
check "AC01 the outcome exposes the best-ranked candidate" \
  bash -c "grep -q 'Best =>' '$MODEL/DocumentClassificationOutcome.cs'"

# --- AC02: unknown/unsupported is a valid safe outcome ---
check "AC02 DocumentClassificationOutcome carries an explicit Unknown flag" \
  bash -c "grep -q 'bool Unknown' '$MODEL/DocumentClassificationOutcome.cs'"
check "AC02 a below-threshold or empty candidate set is reported unknown, never forced as a pick" \
  bash -c "grep -q 'MinAcceptableConfidence' '$MODEL/Classifier.cs' && grep -q 'no_candidate_matched' '$MODEL/Classifier.cs'"
check "AC02 an unknown outcome never selects a template" \
  bash -c "grep -q 'selectedTemplateId = unknown ? null' '$MODEL/Classifier.cs'"

# --- AC03: template selection uses versioned Document Intelligence Registry entries ---
check "AC03 DocumentIntelligenceRegistry exists" test -f "$MODEL/DocumentIntelligenceRegistry.cs"
check "AC03 DocumentIntelligenceRegistryEntry carries a VersionNumber" \
  bash -c "grep -q 'VersionNumber' '$MODEL/DocumentIntelligenceRegistryEntry.cs'"
check "AC03 registering a stale/equal version is rejected, never silently overwritten" \
  bash -c "grep -q 'StaleRegistryVersionException' '$MODEL/DocumentIntelligenceRegistry.cs'"
check "AC03 the classifier resolves its selected template from a registry entry" \
  bash -c "grep -q 'GetByTemplateId' '$MODEL/Classifier.cs'"

# --- Acceptance finding (BIO-DOC-006-QA-01, High): superseded registry templates remained selectable ---
check "QA-01 upgrading a version retires the superseded entry's template from the reverse lookup" \
  bash -c "grep -q '_byTemplateId.Remove(existing.TemplateId)' '$MODEL/DocumentIntelligenceRegistry.cs'"

# --- Acceptance finding (BIO-DOC-006-QA-02, High): one template could silently identify two current identities ---
check "QA-02 registering an already-owned template checks its current owner identity" \
  bash -c "grep -q '_byTemplateId.TryGetValue(entry.TemplateId' '$MODEL/DocumentIntelligenceRegistry.cs'"
check "QA-02 a template reassigned to a new identity evicts the former owner from the identity index" \
  bash -c "grep -q '_byIdentity.Remove(ownerKey)' '$MODEL/DocumentIntelligenceRegistry.cs'"

# --- AC04: wrong-side/front-back detection is supported ---
check "AC04 DocumentSide model exists" test -f "$MODEL/DocumentSide.cs"
check "AC04 registry entries declare which side they describe" \
  bash -c "grep -q 'DocumentSide Side' '$MODEL/DocumentIntelligenceRegistryEntry.cs'"
check "AC04 the outcome reports both detected and expected side plus a mismatch flag" \
  bash -c "grep -q 'DetectedSide' '$MODEL/DocumentClassificationOutcome.cs' && grep -q 'SideMismatch' '$MODEL/DocumentClassificationOutcome.cs'"
check "AC04 a mismatch is only ever detected against a caller-asserted expected side, never guessed" \
  bash -c "grep -q 'signals.ExpectedSide is { } expected' '$MODEL/Classifier.cs'"

# --- AC05: low confidence requires operator confirmation ---
check "AC05 DocumentClassificationOutcome carries RequiresOperatorConfirmation" \
  bash -c "grep -q 'bool RequiresOperatorConfirmation' '$MODEL/DocumentClassificationOutcome.cs'"
check "AC05 confirmation reasons are reported, not just a bare flag" \
  bash -c "grep -q 'ConfirmationReasons' '$MODEL/DocumentClassificationOutcome.cs'"
check "AC05 an ambiguous top-two candidate score forces confirmation, never a silent pick" \
  bash -c "grep -q 'ambiguous_top_candidates' '$MODEL/Classifier.cs'"
check "AC05 a winning template match that itself required manual review forces confirmation" \
  bash -c "grep -q 'template_match_requires_review' '$MODEL/Classifier.cs'"

# --- QA-03 lesson carried forward: trust-bearing result types cannot be built or copy-modified externally ---
check "DocumentClassCandidate properties are internal init, not public init" \
  bash -c "! grep -qE '\{ get; init; \}' '$MODEL/DocumentClassCandidate.cs'"
check "DocumentClassificationOutcome properties are internal init, not public init" \
  bash -c "! grep -qE '\{ get; init; \}' '$MODEL/DocumentClassificationOutcome.cs'"
check "standing reflection-based lockdown regression test exists" \
  test -f tests/features/bio-doc-006/Biolog.Features.DocumentClassifier.Tests/DocumentClassificationResultTypesAreLockedDownTests.cs

# --- Dependency direction: this feature depends on BIO-DOC-002/003/004 only, never BIO-DOC-001/005 ---
check "DocumentClassifier references BIO-DOC-002's MRZ engine" \
  bash -c "grep -q 'Biolog.Features.MrzEngine.csproj' '$FEATURE/Biolog.Features.DocumentClassifier.csproj'"
check "DocumentClassifier references BIO-DOC-003's barcode engine" \
  bash -c "grep -q 'Biolog.Features.BarcodeEngine.csproj' '$FEATURE/Biolog.Features.DocumentClassifier.csproj'"
check "DocumentClassifier references BIO-DOC-004's OCR engine" \
  bash -c "grep -q 'Biolog.Features.OcrEngine.csproj' '$FEATURE/Biolog.Features.DocumentClassifier.csproj'"
check "DocumentClassifier does NOT reference BIO-DOC-001 (not a declared dependency of this story)" \
  bash -c "! grep -q 'bio-doc-001' '$FEATURE/Biolog.Features.DocumentClassifier.csproj'"
check "DocumentClassifier does NOT reference BIO-DOC-005 (not a declared dependency of this story)" \
  bash -c "! grep -q 'bio-doc-005' '$FEATURE/Biolog.Features.DocumentClassifier.csproj'"
check "no sibling BIO-DOC-00x project takes a dependency back on this feature (no cycle)" \
  bash -c "! grep -rq 'bio-doc-006' api/features/bio-doc-002/*/*.csproj api/features/bio-doc-003/*/*.csproj api/features/bio-doc-004/*/*.csproj"
check "no ASP.NET Core / Biolog.Api project or framework reference was added to this feature" \
  bash -c "! grep -qE '<(ProjectReference|FrameworkReference)[^>]*(Biolog\.Api|Microsoft\.AspNetCore)' '$FEATURE/Biolog.Features.DocumentClassifier.csproj'"

# --- No new canonical identifier prefix invented locally (ADR-0001 S1.2); shared contracts untouched ---
check "ADR-0001-S1.2 the frozen identifier registry was not touched by this story" \
  bash -c "! git diff --cached --name-only HEAD -- contracts/ 2>/dev/null | grep -q . && ! git diff --name-only HEAD -- contracts/ 2>/dev/null | grep -q ."

# --- Test/evidence/traceability scaffolding exists ---
check "feature test project exists" test -f tests/features/bio-doc-006/Biolog.Features.DocumentClassifier.Tests/Biolog.Features.DocumentClassifier.Tests.csproj
check "story traceability spec exists" test -f governance/stories/BIO-DOC-006.json
check "feature design doc exists" test -f docs/features/bio-doc-006/DOCUMENT-CLASSIFIER.md

echo "----"
echo "BIO-DOC-006 structural checks: $pass passed, $fail failed"
[ "$fail" -eq 0 ]
