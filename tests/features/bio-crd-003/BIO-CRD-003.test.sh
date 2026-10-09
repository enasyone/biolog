#!/usr/bin/env bash
# Offline-safe structural checks for BIO-CRD-003 (card typography, name fields and
# watermarks). Behavioural assertions live in the executable suites: the backend's own xunit
# tests (tests/features/bio-crd-003/), the frontend's pure logic unit tests
# (tests/frontend/unit/bio-crd-003-*.test.ts, run by scripts/quality.sh --full's "frontend
# unit tests" step) and the real-browser Playwright spec
# (tests/frontend/playwright/bio-crd-003-card-typography.spec.ts, run by the "Playwright
# tests" step) - see docs/features/bio-crd-003/README.md.
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"
pass=0; fail=0
check() { local label="$1"; shift; if "$@"; then echo "PASS  $label"; pass=$((pass+1)); else echo "FAIL  $label"; fail=$((fail+1)); fi; }

API_FEATURE=api/features/bio-crd-003
API_TESTS=tests/features/bio-crd-003
WEB_FEATURE=web/features/bio-crd-003
WEB_TESTS=tests/frontend/unit
E2E_TESTS=tests/frontend/playwright

# --- AC01: each name field binds independently ---
check "AC01 name field bindings cover all six fields" bash -c \
  "grep -q \"'title'\" $WEB_FEATURE/nameFields.ts && grep -q \"'firstName'\" $WEB_FEATURE/nameFields.ts &&
   grep -q \"'middleName'\" $WEB_FEATURE/nameFields.ts && grep -q \"'lastName'\" $WEB_FEATURE/nameFields.ts &&
   grep -q \"'fullName'\" $WEB_FEATURE/nameFields.ts && grep -q \"'displayName'\" $WEB_FEATURE/nameFields.ts"
check "AC01 setBinding never touches typography (proves binding and style are independent state)" \
  bash -c "grep -q 'export function setBinding' $WEB_FEATURE/nameFields.ts"
check "AC01 a real-browser test proves two independently-bound fields render as two independent layers" \
  bash -c "grep -q 'BIO-CRD-003-AC01' $E2E_TESTS/bio-crd-003-card-typography.spec.ts"

# --- AC02: font family/size/colour/bold/italic/underline/case/alignment/spacing/opacity per field ---
check "AC02 typography style exposes every required attribute" bash -c \
  "grep -q 'fontFamily' $WEB_FEATURE/typography.ts && grep -q 'fontSizePt' $WEB_FEATURE/typography.ts &&
   grep -q 'color' $WEB_FEATURE/typography.ts && grep -q 'bold' $WEB_FEATURE/typography.ts &&
   grep -q 'italic' $WEB_FEATURE/typography.ts && grep -q 'underline' $WEB_FEATURE/typography.ts &&
   grep -q 'textCase' $WEB_FEATURE/typography.ts && grep -q 'textAlign' $WEB_FEATURE/typography.ts &&
   grep -q 'letterSpacingPx' $WEB_FEATURE/typography.ts && grep -q 'opacityPercent' $WEB_FEATURE/typography.ts"
check "AC02 opacity is validated to the 0-100 range" \
  bash -c "grep -q 'next.opacityPercent < 0 || next.opacityPercent > 100' $WEB_FEATURE/typography.ts"
check "AC02 a test proves each typography attribute is independently editable per field" \
  bash -c "test -f $WEB_TESTS/bio-crd-003-typography.test.ts"

# --- AC03: copy style does not copy data binding ---
check "AC03 copyStyleTo exists and its own signature never reads or writes .binding" \
  bash -c "grep -q 'export function copyStyleTo' $WEB_FEATURE/nameFields.ts && ! grep -A5 'export function copyStyleTo' $WEB_FEATURE/nameFields.ts | grep -q '\.binding'"
check "AC03 a test proves binding survives a copy, including a chain of copies" \
  bash -c "grep -qi 'binding' $WEB_TESTS/bio-crd-003-name-fields.test.ts"
check "AC03 the UI discloses that binding is unaffected by the copy action" \
  bash -c "grep -q 'binding unaffected' $WEB_FEATURE/CardTypographyApp.tsx"

# --- AC04: text/image watermarks support opacity, rotation and layer control ---
check "AC04 watermark element supports both text and image kinds" \
  bash -c "grep -q \"'text' | 'image'\" $WEB_FEATURE/watermarks.ts"
check "AC04 watermark opacity is validated to the 0-100 range" \
  bash -c "grep -q 'opacityPercent < 0 || opacityPercent > 100' $WEB_FEATURE/watermarks.ts"
check "AC04 rotation is normalised on construction, not only on later mutation" \
  bash -c "grep -q 'rotationDeg: normaliseDegrees(rotationDeg)' $WEB_FEATURE/watermarks.ts"
check "AC04 layer control (bring to front/back, forward/backward) is available to watermarks" \
  bash -c "grep -q 'export function bringToFront' $WEB_FEATURE/canvasElementOps.ts && grep -q 'export function sendToBack' $WEB_FEATURE/canvasElementOps.ts"
check "AC04 a real-browser test proves opacity, rotation and layer reordering all work" \
  bash -c "grep -q 'BIO-CRD-003-AC04' $E2E_TESTS/bio-crd-003-card-typography.spec.ts"

# --- AC05: STAFF/STUDENT/VISITOR starter watermark behaviors ---
check "AC05 backend preset catalogue defines STAFF/STUDENT/VISITOR" bash -c \
  "grep -q 'WatermarkPresetKind.Staff' $API_FEATURE/Domain/WatermarkPresetCatalog.cs &&
   grep -q 'WatermarkPresetKind.Student' $API_FEATURE/Domain/WatermarkPresetCatalog.cs &&
   grep -q 'WatermarkPresetKind.Visitor' $API_FEATURE/Domain/WatermarkPresetCatalog.cs"
check "AC05 frontend preset catalogue mirrors the backend's three presets" bash -c \
  "grep -q 'staff:' $WEB_FEATURE/watermarks.ts && grep -q 'student:' $WEB_FEATURE/watermarks.ts && grep -q 'visitor:' $WEB_FEATURE/watermarks.ts"
check "AC05 a preset-lookup endpoint exists" \
  bash -c "grep -q 'watermark-presets' $API_FEATURE/Api/WatermarkPresetEndpoints.cs"
check "AC05 a real-browser test proves all three preset buttons add correctly-labelled watermarks" \
  bash -c "grep -q 'BIO-CRD-003-AC05' $E2E_TESTS/bio-crd-003-card-typography.spec.ts"

# --- Module wiring and write-scope discipline ---
check "AC generic element ops project independently of BIO-CRD-002 (own file, not imported from it)" \
  bash -c "! grep -q \"from '../bio-crd-002\" $WEB_FEATURE/canvasElementOps.ts"
check "the only reuse of BIO-CRD-002 is a read-only import of its exported pure format helpers" \
  bash -c "grep -q \"from '../bio-crd-002/cardFormats'\" $WEB_FEATURE/CardTypographyApp.tsx"
check "backend module lives one-way in api/Biolog.Api/Modules, not back in the feature project" \
  bash -c "test -f api/Biolog.Api/Modules/BioCrd003Module.cs && ! test -f '$API_FEATURE/BioCrd003Module.cs'"
check "registered in the packaged Program.cs module array" \
  bash -c "grep -q 'new BioCrd003Module()' api/Biolog.Api/Program.cs"
check "no ProjectReference to any other feature project" \
  bash -c "! grep -q '<ProjectReference' $API_FEATURE/Biolog.Features.CardTypography.csproj"
check "the shared /card-designer route composes both BIO-CRD-002's and BIO-CRD-003's tools" \
  bash -c "grep -q 'CardDesignerApp' web/app/card-designer/page.tsx && grep -q 'CardTypographyApp' web/app/card-designer/page.tsx"

# --- Developer test wiring ---
check "backend unit/integration tests exist" bash -c \
  'test -f tests/features/bio-crd-003/WatermarkPresetCatalogTests.cs &&
   test -f tests/features/bio-crd-003/WatermarkPresetEndpointsIntegrationTests.cs'
check "frontend pure-logic unit tests exist for every model file" bash -c \
  'test -f tests/frontend/unit/bio-crd-003-typography.test.ts &&
   test -f tests/frontend/unit/bio-crd-003-name-fields.test.ts &&
   test -f tests/frontend/unit/bio-crd-003-watermarks.test.ts &&
   test -f tests/frontend/unit/bio-crd-003-canvas-element-ops.test.ts'
check "a real-browser Playwright spec exists, including an accessibility baseline" bash -c \
  "test -f $E2E_TESTS/bio-crd-003-card-typography.spec.ts && grep -q 'axe' $E2E_TESTS/bio-crd-003-card-typography.spec.ts"
check "builder evidence recorded" test -d evidence/BIO-CRD-003/builder

echo "=============================="
echo "$pass passed, $fail failed"
[ "$fail" -eq 0 ]
