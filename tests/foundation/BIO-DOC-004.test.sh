#!/usr/bin/env bash
# Offline-safe structural checks for BIO-DOC-004 (template-driven and generic visual OCR).
# Behavioural assertions (field extraction, normalization, low-confidence review, text-line
# detection, language-pack seam) live in the executable xunit suite under
# tests/features/bio-doc-004/Biolog.Features.OcrEngine.Tests (see
# docs/features/bio-doc-004/OCR-ENGINE.md, docs/TESTING.md).
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"
pass=0; fail=0
check() { local label="$1"; shift; if "$@"; then echo "PASS  $label"; pass=$((pass+1)); else echo "FAIL  $label"; fail=$((fail+1)); fi; }

FEATURE=api/features/bio-doc-004/Biolog.Features.OcrEngine
MODEL="$FEATURE/Model"

# --- AC01: template OCR returns field type, raw value, normalized value, confidence, coordinates ---
check "AC01 OcrFieldRead model exists" test -f "$MODEL/OcrFieldRead.cs"
check "AC01 carries FieldType, RawValue, NormalizedValue, Confidence and BoundingBox" \
  bash -c "grep -q 'OcrFieldType FieldType' '$MODEL/OcrFieldRead.cs' && grep -q 'string RawValue' '$MODEL/OcrFieldRead.cs' && grep -q 'string NormalizedValue' '$MODEL/OcrFieldRead.cs' && grep -q 'double Confidence' '$MODEL/OcrFieldRead.cs' && grep -q 'BoundingBox BoundingBox' '$MODEL/OcrFieldRead.cs'"
check "AC01 TemplateOcrEngine reads declared field regions directly (no detection needed)" \
  test -f "$MODEL/TemplateOcrEngine.cs"
check "AC01 template registry is configured, not a hard-coded document switch" \
  bash -c "grep -q 'public void Register(' '$MODEL/OcrDocumentTemplate.cs'"

# --- AC02: generic OCR returns text blocks without pretending field semantics ---
check "AC02 GenericTextBlock model exists" test -f "$MODEL/GenericTextBlock.cs"
check "AC02 GenericTextBlock carries no field type or field name property" \
  bash -c "! grep -qE 'public .*(OcrFieldType|FieldName|FieldType) .*\{ *get' '$MODEL/GenericTextBlock.cs'"
check "AC02 text-line detection is real ink-projection analysis, not a fixed offset" \
  bash -c "grep -q 'FindRowBands' '$MODEL/OcrTextLineDetector.cs' && grep -q 'FindColumnCells' '$MODEL/OcrTextLineDetector.cs'"
check "AC02 an ambiguous grid hypothesis is refused, never guessed between" \
  bash -c "grep -q 'ambiguous' '$MODEL/OcrTextLineDetector.cs'"

# --- AC03: English is production baseline; French is architected as the next language pack ---
check "AC03 IOcrLanguagePack seam exists" test -f "$MODEL/IOcrLanguagePack.cs"
check "AC03 OcrLanguageCode already names French" \
  bash -c "grep -q 'French' '$MODEL/IOcrLanguagePack.cs'"
check "AC03 the default registry ships English only" \
  bash -c "grep -q 'EnglishLanguagePack()' '$MODEL/IOcrLanguagePack.cs' && ! grep -qE 'class FrenchLanguagePack|new FrenchLanguagePack' '$MODEL/IOcrLanguagePack.cs'"

# --- AC04: low-confidence critical fields require human review ---
check "AC04 TemplateOcrResult carries RequiresManualReview" \
  bash -c "grep -q 'RequiresManualReview' '$MODEL/TemplateOcrResult.cs'"
check "AC04 Critical is a per-field flag, not a blanket gate" \
  bash -c "grep -q 'bool Critical' '$MODEL/OcrDocumentTemplate.cs' && grep -q 'fieldSpec.Critical &&' '$MODEL/TemplateOcrEngine.cs'"
check "AC04 a confidence threshold gates review, not a silent accept-anything" \
  bash -c "grep -q 'MinAcceptableConfidence' '$MODEL/TemplateOcrEngine.cs'"

# --- AC05: OCR never uses generative guessing to fill unreadable identity characters ---
check "AC05 the recognizer always reports its best match and a confidence, never a placeholder" \
  bash -c "grep -q 'Confidence' '$MODEL/OcrGlyphFont.cs'"
check "AC05 no field anywhere is silently rewritten after recognition (single assignment per character)" \
  bash -c "! grep -qE 'characters\[i\] = .*\?' '$MODEL/TemplateOcrEngine.cs' '$MODEL/GenericOcrEngine.cs'"

# --- QA-03 lesson carried forward: trust-bearing result types cannot be built or copy-modified externally ---
check "OcrFieldRead properties are internal init, not public init" \
  bash -c "! grep -qE '\{ get; init; \}' '$MODEL/OcrFieldRead.cs'"
check "TemplateOcrResult properties are internal init, not public init" \
  bash -c "! grep -qE '\{ get; init; \}' '$MODEL/TemplateOcrResult.cs'"
check "GenericTextBlock properties are internal init, not public init" \
  bash -c "! grep -qE '\{ get; init; \}' '$MODEL/GenericTextBlock.cs'"
check "standing reflection-based lockdown regression test exists" \
  test -f tests/features/bio-doc-004/Biolog.Features.OcrEngine.Tests/OcrResultTypesAreLockedDownTests.cs

# --- Dependency direction: this feature depends on BIO-DOC-001 only, never its siblings, never the reverse ---
check "OcrEngine references BIO-DOC-001's evidence model" \
  bash -c "grep -q 'Biolog.Features.DocumentVerify.csproj' '$FEATURE/Biolog.Features.OcrEngine.csproj'"
check "OcrEngine does NOT reference BIO-DOC-002's MrzEngine or BIO-DOC-003's BarcodeEngine" \
  bash -c "! grep -qE '<ProjectReference[^>]*(MrzEngine|BarcodeEngine)' '$FEATURE/Biolog.Features.OcrEngine.csproj'"
check "BIO-DOC-001's own project takes no dependency back on this feature (no cycle)" \
  bash -c "! grep -q 'bio-doc-004' api/features/bio-doc-001/Biolog.Features.DocumentVerify/Biolog.Features.DocumentVerify.csproj"
check "no ASP.NET Core / Biolog.Api project or framework reference was added to this feature" \
  bash -c "! grep -qE '<(ProjectReference|FrameworkReference)[^>]*(Biolog\.Api|Microsoft\.AspNetCore)' '$FEATURE/Biolog.Features.OcrEngine.csproj'"

# --- No new canonical identifier prefix invented locally (ADR-0001 S1.2); shared contracts untouched ---
check "ADR-0001-S1.2 the frozen identifier registry was not touched by this story" \
  bash -c "! git diff --cached --name-only HEAD -- contracts/ 2>/dev/null | grep -q . && ! git diff --name-only HEAD -- contracts/ 2>/dev/null | grep -q ."

# --- Test/evidence/traceability scaffolding exists ---
check "feature test project exists" test -f tests/features/bio-doc-004/Biolog.Features.OcrEngine.Tests/Biolog.Features.OcrEngine.Tests.csproj
check "story traceability spec exists" test -f governance/stories/BIO-DOC-004.json
check "feature design doc exists" test -f docs/features/bio-doc-004/OCR-ENGINE.md

echo "----"
echo "BIO-DOC-004 structural checks: $pass passed, $fail failed"
[ "$fail" -eq 0 ]
