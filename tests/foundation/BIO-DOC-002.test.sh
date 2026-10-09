#!/usr/bin/env bash
# Offline-safe structural checks for BIO-DOC-002 (native MRZ detection, recognition, parsing and
# validation). Behavioural assertions (detection geometry, check-digit math, field extraction,
# manual-review derivation) live in the executable xunit suite under
# tests/features/bio-doc-002/Biolog.Features.MrzEngine.Tests (see
# docs/features/bio-doc-002/MRZ-ENGINE.md, docs/TESTING.md).
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"
pass=0; fail=0
check() { local label="$1"; shift; if "$@"; then echo "PASS  $label"; pass=$((pass+1)); else echo "FAIL  $label"; fail=$((fail+1)); fi; }

FEATURE=api/features/bio-doc-002/Biolog.Features.MrzEngine
MODEL="$FEATURE/Model"

# --- AC01: MRZ region can be detected from synthetic/reference images ---
check "AC01 DocumentRaster model exists" test -f "$MODEL/DocumentRaster.cs"
check "AC01 no image-decoding/CV/ML package reference was added" \
  bash -c "! grep -qiE 'SkiaSharp|ImageSharp|OpenCvSharp|System.Drawing|ML.NET' '$FEATURE/Biolog.Features.MrzEngine.csproj'"
check "AC01 MrzRegionDetector model exists" test -f "$MODEL/MrzRegionDetector.cs"
check "AC01 detection is row/column ink-projection profiling, not a fixed offset" \
  bash -c "grep -q 'FindRowBands' '$MODEL/MrzRegionDetector.cs' && grep -q 'FindColumnCells' '$MODEL/MrzRegionDetector.cs'"
check "AC01 an undetected page is reported, never defaulted to a guessed region" \
  bash -c "grep -q 'mrz_region_not_detected' '$MODEL/MrzRegionDetector.cs'"
check "AC01 two equally valid candidate blocks are refused, not picked between" \
  bash -c "grep -q 'multiple_mrz_candidates' '$MODEL/MrzRegionDetector.cs'"

# --- AC02: TD1/TD2/TD3 and configured visa formats are parsed ---
check "AC02 MrzFormat/MrzFormatRegistry model exists" test -f "$MODEL/MrzFormat.cs"
check "AC02 TD1, TD2 and TD3 are registered" \
  bash -c "grep -q 'MrzFormatKind.Td1' '$MODEL/MrzFormat.cs' && grep -q 'MrzFormatKind.Td2' '$MODEL/MrzFormat.cs' && grep -q 'MrzFormatKind.Td3' '$MODEL/MrzFormat.cs'"
check "AC02 visa formats are registered as configuration, not a separate hard-coded engine path" \
  bash -c "grep -q 'MrzFormatKind.MrvA' '$MODEL/MrzFormat.cs' && grep -q 'MrzFormatKind.MrvB' '$MODEL/MrzFormat.cs'"
check "AC02 a Register() seam exists for adding a further configured format" \
  bash -c "grep -q 'public static void Register(' '$MODEL/MrzFormat.cs'"
check "AC02 MrzParser model exists" test -f "$MODEL/MrzParser.cs"

# --- AC03: check digits are recalculated and reported independently ---
check "AC03 MrzCheckDigit model exists" test -f "$MODEL/MrzCheckDigit.cs"
check "AC03 uses the ICAO 7/3/1 weight cycle" bash -c "grep -q '7, 3, 1' '$MODEL/MrzCheckDigit.cs'"
check "AC03 a field carries both the read and independently computed check digit" \
  bash -c "grep -q 'ReadCheckDigit' '$MODEL/MrzFieldRead.cs' && grep -q 'ComputedCheckDigit' '$MODEL/MrzFieldRead.cs'"
check "AC03 check-digit computation never treats an unrecognized character as filler" \
  bash -c "grep -q 'ArgumentOutOfRangeException' '$MODEL/MrzCheckDigit.cs'"

# --- AC04: raw recognition and normalized values are both preserved ---
check "AC04 MrzFieldRead preserves RawValue and NormalizedValue separately" \
  bash -c "grep -q 'RawValue' '$MODEL/MrzFieldRead.cs' && grep -q 'NormalizedValue' '$MODEL/MrzFieldRead.cs'"
check "AC04 a parsed field adapts into BIO-DOC-001's DocumentFieldEvidence via FieldEvidenceSource.Mrz" \
  bash -c "grep -q 'ToDocumentFieldEvidence' '$MODEL/MrzFieldRead.cs' && grep -q 'FieldEvidenceSource.Mrz' '$MODEL/MrzFieldRead.cs'"
check "AC04 recognition always reports a confidence, never a silent pass-through of given input" \
  bash -c "grep -q 'Confidence' '$MODEL/MrzGlyphFont.cs'"

# --- AC05: low-confidence/invalid results require review rather than guessed correction ---
check "AC05 MrzReadOutcome carries RequiresManualReview" \
  bash -c "grep -q 'RequiresManualReview' '$MODEL/MrzReadOutcome.cs'"
check "AC05 a confidence threshold gates review, not a silent accept-anything" \
  bash -c "grep -q 'MinAcceptableConfidence' '$MODEL/MrzParser.cs'"
check "AC05 a check-digit mismatch is reported by name, never resolved by picking a side" \
  bash -c "grep -q 'check_digit_mismatch' '$MODEL/MrzParser.cs'"

# --- QA-03 lesson carried forward: trust-bearing result types cannot be built or copy-modified externally ---
check "MrzFieldRead exposes no public constructor (internal-only, no validation to bypass but nothing to fabricate either)" \
  bash -c "! grep -qE '^\s*public MrzFieldRead\(' '$MODEL/MrzFieldRead.cs'"
check "MrzFieldRead properties are internal init, not public init" \
  bash -c "! grep -qE '\{ get; init; \}' '$MODEL/MrzFieldRead.cs'"
check "MrzReadOutcome properties are internal init, not public init" \
  bash -c "! grep -qE '\{ get; init; \}' '$MODEL/MrzReadOutcome.cs'"
check "standing reflection-based lockdown regression test exists" \
  test -f tests/features/bio-doc-002/Biolog.Features.MrzEngine.Tests/MrzResultTypesAreLockedDownTests.cs

# --- Dependency direction: this feature depends on BIO-DOC-001, never the reverse ---
check "MrzEngine references BIO-DOC-001's evidence model" \
  bash -c "grep -q 'Biolog.Features.DocumentVerify.csproj' '$FEATURE/Biolog.Features.MrzEngine.csproj'"
check "BIO-DOC-001's own project takes no dependency back on this feature (no cycle)" \
  bash -c "! grep -q 'bio-doc-002' api/features/bio-doc-001/Biolog.Features.DocumentVerify/Biolog.Features.DocumentVerify.csproj"
check "no ASP.NET Core / Biolog.Api project or framework reference was added to this feature" \
  bash -c "! grep -qE '<(ProjectReference|FrameworkReference)[^>]*(Biolog\.Api|Microsoft\.AspNetCore)' '$FEATURE/Biolog.Features.MrzEngine.csproj'"

# --- No new canonical identifier prefix invented locally (ADR-0001 S1.2); shared contracts untouched ---
check "ADR-0001-S1.2 the frozen identifier registry was not touched by this story" \
  bash -c "! git diff --cached --name-only HEAD -- contracts/ 2>/dev/null | grep -q . && ! git diff --name-only HEAD -- contracts/ 2>/dev/null | grep -q ."

# --- Test/evidence/traceability scaffolding exists ---
check "feature test project exists" test -f tests/features/bio-doc-002/Biolog.Features.MrzEngine.Tests/Biolog.Features.MrzEngine.Tests.csproj
check "story traceability spec exists" test -f governance/stories/BIO-DOC-002.json
check "feature design doc exists" test -f docs/features/bio-doc-002/MRZ-ENGINE.md

echo "----"
echo "BIO-DOC-002 structural checks: $pass passed, $fail failed"
[ "$fail" -eq 0 ]
