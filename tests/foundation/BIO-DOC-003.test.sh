#!/usr/bin/env bash
# Offline-safe structural checks for BIO-DOC-003 (native barcode detection/decoding service).
# Behavioural assertions (decoding, detection signatures, hostile-input handling, hash-not-payload)
# live in the executable xunit suite under tests/features/bio-doc-003/Biolog.Features.BarcodeEngine.Tests
# (see docs/features/bio-doc-003/BARCODE-ENGINE.md, docs/TESTING.md).
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"
pass=0; fail=0
check() { local label="$1"; shift; if "$@"; then echo "PASS  $label"; pass=$((pass+1)); else echo "FAIL  $label"; fail=$((fail+1)); fi; }

FEATURE=api/features/bio-doc-003/Biolog.Features.BarcodeEngine
MODEL="$FEATURE/Model"

# --- AC01: QR/PDF417/DataMatrix/Aztec/Code128/Code39 supported via replaceable decoder interfaces ---
check "AC01 IBarcodeDecoder interface exists" test -f "$MODEL/IBarcodeDecoder.cs"
check "AC01 BarcodeSymbology names all six required symbologies" \
  bash -c "grep -q 'Qr,' '$MODEL/BarcodeSymbology.cs' && grep -q 'Pdf417,' '$MODEL/BarcodeSymbology.cs' && grep -q 'DataMatrix,' '$MODEL/BarcodeSymbology.cs' && grep -q 'Aztec,' '$MODEL/BarcodeSymbology.cs' && grep -q 'Code128,' '$MODEL/BarcodeSymbology.cs' && grep -q 'Code39,' '$MODEL/BarcodeSymbology.cs'"
check "AC01 a decoder registry exists with a Register() swap seam" \
  bash -c "grep -q 'public void Register(' '$MODEL/BarcodeDecoderRegistry.cs'"
check "AC01 the default registry registers all six symbologies' decoders" \
  bash -c "grep -q 'Code39Decoder()' '$MODEL/BarcodeDecoderRegistry.cs' && grep -q 'Code128SignatureDecoder()' '$MODEL/BarcodeDecoderRegistry.cs' && grep -q 'QrSignatureDecoder()' '$MODEL/BarcodeDecoderRegistry.cs' && grep -q 'DataMatrixSignatureDecoder()' '$MODEL/BarcodeDecoderRegistry.cs' && grep -q 'AztecSignatureDecoder()' '$MODEL/BarcodeDecoderRegistry.cs' && grep -q 'Pdf417SignatureDecoder()' '$MODEL/BarcodeDecoderRegistry.cs'"
check "AC01 orchestration service exists and isolates a per-decoder failure" \
  bash -c "grep -q 'class BarcodeDetectionService' '$MODEL/BarcodeDetectionService.cs' && grep -q 'catch (Exception)' '$MODEL/BarcodeDetectionService.cs'"
check "AC01 Code 39 is fully decoded, not detection-only" \
  bash -c "grep -q 'BarcodeDecodeAttempt.Decode(' '$MODEL/Code39Decoder.cs'"

# --- AC02: symbology, bounding box, raw payload hash and decode confidence ---
check "AC02 BarcodeDecodeResult model exists" test -f "$MODEL/BarcodeDecodeResult.cs"
check "AC02 result carries symbology, bounding box, a hash and confidence" \
  bash -c "grep -q 'BarcodeSymbology Symbology' '$MODEL/BarcodeDecodeResult.cs' && grep -q 'BoundingBox BoundingBox' '$MODEL/BarcodeDecodeResult.cs' && grep -q 'RawPayloadHash' '$MODEL/BarcodeDecodeResult.cs' && grep -q 'double Confidence' '$MODEL/BarcodeDecodeResult.cs'"
check "AC02 the raw payload itself is never a property of the result (hash only)" \
  bash -c "! grep -qE 'byte\[\]\?? +RawPayload +\{' '$MODEL/BarcodeDecodeResult.cs'"
check "AC02 the hash is computed via SHA-256" bash -c "grep -q 'SHA256' '$MODEL/BarcodeDecodeResult.cs'"

# --- AC03: hostile/malformed payloads are treated as inert data ---
check "AC03 Code39Decoder never returns a partial/best-guess character on a width mismatch" \
  bash -c "grep -q 'return null; // neither' '$MODEL/Code39Decoder.cs'"
check "AC03 malformed start/stop framing is rejected, not partially accepted" \
  bash -c "grep -q 'StartStop' '$MODEL/Code39Decoder.cs'"
check "AC03 the service isolates one decoder's exception from the rest of the scan" \
  bash -c "grep -q 'TryDecodeSafely' '$MODEL/BarcodeDetectionService.cs'"

# --- AC04: no decoded barcode alone is labelled authentic ---
check "AC04 no IsAuthentic/IsValid-shaped field on the result type" \
  bash -c "! grep -qiE 'bool +Is ?Authentic|bool +Is ?Genuine' '$MODEL/BarcodeDecodeResult.cs' '$MODEL/BarcodeDecodeAttempt.cs'"

# --- QA-03 lesson carried forward: trust-bearing result types cannot be built or copy-modified externally ---
check "BarcodeDecodeResult properties are internal init, not public init" \
  bash -c "! grep -qE '\{ get; init; \}' '$MODEL/BarcodeDecodeResult.cs'"
check "BarcodeDecodeAttempt properties are internal init, not public init" \
  bash -c "! grep -qE '\{ get; init; \}' '$MODEL/BarcodeDecodeAttempt.cs'"
check "standing reflection-based lockdown regression test exists" \
  test -f tests/features/bio-doc-003/Biolog.Features.BarcodeEngine.Tests/BarcodeResultTypesAreLockedDownTests.cs

# --- Dependency direction: this feature depends on BIO-DOC-001 only, never BIO-DOC-002, never the reverse ---
check "BarcodeEngine references BIO-DOC-001's evidence model" \
  bash -c "grep -q 'Biolog.Features.DocumentVerify.csproj' '$FEATURE/Biolog.Features.BarcodeEngine.csproj'"
check "BarcodeEngine does NOT reference BIO-DOC-002's MrzEngine (not a declared dependency)" \
  bash -c "! grep -qE '<ProjectReference[^>]*MrzEngine' '$FEATURE/Biolog.Features.BarcodeEngine.csproj'"
check "BIO-DOC-001's own project takes no dependency back on this feature (no cycle)" \
  bash -c "! grep -q 'bio-doc-003' api/features/bio-doc-001/Biolog.Features.DocumentVerify/Biolog.Features.DocumentVerify.csproj"
check "no ASP.NET Core / Biolog.Api project or framework reference was added to this feature" \
  bash -c "! grep -qE '<(ProjectReference|FrameworkReference)[^>]*(Biolog\.Api|Microsoft\.AspNetCore)' '$FEATURE/Biolog.Features.BarcodeEngine.csproj'"

# --- No new canonical identifier prefix invented locally (ADR-0001 S1.2); shared contracts untouched ---
check "ADR-0001-S1.2 the frozen identifier registry was not touched by this story" \
  bash -c "! git diff --cached --name-only HEAD -- contracts/ 2>/dev/null | grep -q . && ! git diff --name-only HEAD -- contracts/ 2>/dev/null | grep -q ."

# --- Test/evidence/traceability scaffolding exists ---
check "feature test project exists" test -f tests/features/bio-doc-003/Biolog.Features.BarcodeEngine.Tests/Biolog.Features.BarcodeEngine.Tests.csproj
check "story traceability spec exists" test -f governance/stories/BIO-DOC-003.json
check "feature design doc exists" test -f docs/features/bio-doc-003/BARCODE-ENGINE.md

echo "----"
echo "BIO-DOC-003 structural checks: $pass passed, $fail failed"
[ "$fail" -eq 0 ]
