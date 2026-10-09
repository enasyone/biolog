#!/usr/bin/env bash
# Offline-safe structural checks for BIO-CRD-002 (WYSIWYG card canvas and physical card
# formats). Behavioural assertions live in the executable suites: the backend's own xunit
# tests (tests/features/bio-crd-002/), the frontend's pure geometry/model unit tests
# (tests/frontend/unit/bio-crd-002-*.test.ts, run by scripts/quality.sh --full's "frontend
# unit tests" step) and the real-browser Playwright spec
# (tests/frontend/playwright/bio-crd-002-card-designer.spec.ts, run by the "Playwright tests"
# step) - see docs/features/bio-crd-002/README.md.
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"
pass=0; fail=0
check() { local label="$1"; shift; if "$@"; then echo "PASS  $label"; pass=$((pass+1)); else echo "FAIL  $label"; fail=$((fail+1)); fi; }

API_FEATURE=api/features/bio-crd-002
API_TESTS=tests/features/bio-crd-002
WEB_FEATURE=web/features/bio-crd-002
WEB_TESTS=tests/frontend/unit
E2E_TESTS=tests/frontend/playwright

# --- AC01: standard sizes use correct physical dimensions ---
check "AC01 backend feature project exists" test -f "$API_FEATURE/Biolog.Features.CardDesigner.csproj"
check "AC01 backend project targets the pinned .NET SDK's TFM" bash -c "grep -q '<TargetFramework>net10.0</TargetFramework>' '$API_FEATURE/Biolog.Features.CardDesigner.csproj'"
check "AC01 backend catalogue names CR80/CR79/CR100 with their real physical dimensions" bash -c \
  "grep -q '85.60, 53.98' $API_FEATURE/Domain/CardFormatCatalog.cs &&
   grep -q '83.90, 51.90' $API_FEATURE/Domain/CardFormatCatalog.cs &&
   grep -q '98.50, 67.00' $API_FEATURE/Domain/CardFormatCatalog.cs"
check "AC01 frontend format catalogue mirrors the backend's exact dimensions" bash -c \
  "grep -q '85.6' $WEB_FEATURE/cardFormats.ts && grep -q '53.98' $WEB_FEATURE/cardFormats.ts &&
   grep -q '83.9' $WEB_FEATURE/cardFormats.ts && grep -q '98.5' $WEB_FEATURE/cardFormats.ts"
check "AC01 backend module lives one-way in api/Biolog.Api/Modules, not back in the feature project" \
  bash -c "test -f api/Biolog.Api/Modules/BioCrd002Module.cs && ! test -f '$API_FEATURE/BioCrd002Module.cs'"
check "AC01 registered in the packaged Program.cs module array" \
  bash -c "grep -q 'new BioCrd002Module()' api/Biolog.Api/Program.cs"
check "AC01 the real /card-designer route exists (previously a 404 placeholder per docs/MANUAL.md)" \
  test -f web/app/card-designer/page.tsx
check "AC01 a pinning unit test exists for every standard format's dimensions" \
  bash -c "grep -q 'AC01_cr80_matches_the_iso_iec_7810_id_1_dimensions' $API_TESTS/CardFormatCatalogTests.cs &&
           grep -q 'BIO-CRD-002-AC01' $WEB_TESTS/bio-crd-002-card-formats.test.ts"

# --- AC02: landscape/portrait switch preserves geometry without cumulative drift ---
check "AC02 orientation switch uses no trigonometric rotation (only add/subtract of pixel values)" \
  bash -c "! grep -Eq 'Math\.sin|Math\.cos|Math\.PI' $WEB_FEATURE/orientation.ts"
check "AC02 a test proves an exact (not approximate) round trip back to the original state" \
  bash -c "grep -q 'reproduces the EXACT original state' $WEB_TESTS/bio-crd-002-orientation.test.ts"
check "AC02 a test proves many repeated switches never drift from the original" \
  bash -c "grep -q 'switching orientation 50 times' $WEB_TESTS/bio-crd-002-orientation.test.ts"
check "AC02 a real-browser test proves the on-screen canvas returns to its original proportions after switching twice" \
  bash -c "grep -q 'BIO-CRD-002-AC02' $E2E_TESTS/bio-crd-002-card-designer.spec.ts"

# --- AC03: custom width/height validation exists ---
check "AC03 backend custom-size validator exists with named bounds" \
  bash -c "grep -q 'MinEdgeMm' $API_FEATURE/Domain/CustomCardSizeValidator.cs && grep -q 'MaxEdgeMm' $API_FEATURE/Domain/CustomCardSizeValidator.cs"
check "AC03 frontend custom-size validation mirrors the same bounds" \
  bash -c "grep -q 'CUSTOM_SIZE_BOUNDS_MM' $WEB_FEATURE/cardFormats.ts"
check "AC03 a custom-size validation endpoint exists" \
  bash -c "grep -q 'custom-size/validate' $API_FEATURE/Api/CardFormatEndpoints.cs"
check "AC03 an invalid size is data (200), never a 4xx for a well-formed request" \
  bash -c "grep -q 'is DATA, not an HTTP error' $API_FEATURE/Api/CardFormatDtos.cs"

# --- AC04: elements support move/resize/rotate/layer/lock ---
check "AC04 element model exposes move/resize/rotate/lock and layer reordering" bash -c \
  "grep -q 'export function moveElement' $WEB_FEATURE/elements.ts &&
   grep -q 'export function resizeElement' $WEB_FEATURE/elements.ts &&
   grep -q 'export function rotateElement' $WEB_FEATURE/elements.ts &&
   grep -q 'export function setLocked' $WEB_FEATURE/elements.ts &&
   grep -q 'export function bringToFront' $WEB_FEATURE/elements.ts &&
   grep -q 'export function sendToBack' $WEB_FEATURE/elements.ts"
check "AC04 a locked element refuses move/resize/rotate" \
  bash -c "grep -c 'if (element.locked) return element;' $WEB_FEATURE/elements.ts | grep -q '^3$'"
check "AC04 unlocking is never refused, including on an already-locked element" \
  bash -c "grep -q 'harmless no-op, never refused' $WEB_TESTS/bio-crd-002-elements.test.ts"

# --- AC05: front and back are independent ---
check "AC05 every card-design-state operation is explicitly parameterised by side" \
  bash -c "grep -q 'side: CardSide' $WEB_FEATURE/cardDesignState.ts"
check "AC05 a test asserts the untouched side is the same object reference, not merely equal" \
  bash -c "grep -q 'toBe(state.back)' $WEB_TESTS/bio-crd-002-card-design-state.test.ts && grep -q 'toBe(state.front)' $WEB_TESTS/bio-crd-002-card-design-state.test.ts"
check "AC05 a real-browser test proves an element added to one side never appears on the other" \
  bash -c "grep -q 'BIO-CRD-002-AC05' $E2E_TESTS/bio-crd-002-card-designer.spec.ts"

# --- Developer test wiring ---
check "backend unit/integration tests exist" bash -c \
  'test -f tests/features/bio-crd-002/CardFormatCatalogTests.cs &&
   test -f tests/features/bio-crd-002/CustomCardSizeValidatorTests.cs &&
   test -f tests/features/bio-crd-002/CardFormatEndpointsIntegrationTests.cs'
check "frontend pure-logic unit tests exist for every model file" bash -c \
  'test -f tests/frontend/unit/bio-crd-002-card-formats.test.ts &&
   test -f tests/frontend/unit/bio-crd-002-orientation.test.ts &&
   test -f tests/frontend/unit/bio-crd-002-elements.test.ts &&
   test -f tests/frontend/unit/bio-crd-002-card-design-state.test.ts'
check "a real-browser Playwright spec exists, including an accessibility baseline" bash -c \
  "test -f $E2E_TESTS/bio-crd-002-card-designer.spec.ts && grep -q 'axe' $E2E_TESTS/bio-crd-002-card-designer.spec.ts"
check "no ProjectReference to any other feature project (backend has no runtime dependency on BIO-CRD-001/BIO-PHO-001)" \
  bash -c "! grep -q '<ProjectReference' $API_FEATURE/Biolog.Features.CardDesigner.csproj"
check "builder evidence recorded" test -d evidence/BIO-CRD-002/builder

echo "=============================="
echo "$pass passed, $fail failed"
[ "$fail" -eq 0 ]
