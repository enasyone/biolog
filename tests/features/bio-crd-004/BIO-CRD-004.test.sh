#!/usr/bin/env bash
# Offline-safe structural checks for BIO-CRD-004 (dynamic barcode/QR/signature/card objects).
# Behavioural assertions live in the executable suites: the backend's own xunit tests
# (tests/features/bio-crd-004/), the frontend's pure logic unit tests
# (tests/frontend/unit/bio-crd-004-*.test.ts, run by scripts/quality.sh --full's "frontend
# unit tests" step) and the real-browser Playwright spec
# (tests/frontend/playwright/bio-crd-004-card-objects.spec.ts, run by the "Playwright tests"
# step) - see docs/features/bio-crd-004/README.md.
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"
pass=0; fail=0
check() { local label="$1"; shift; if "$@"; then echo "PASS  $label"; pass=$((pass+1)); else echo "FAIL  $label"; fail=$((fail+1)); fi; }

API_FEATURE=api/features/bio-crd-004
API_TESTS=tests/features/bio-crd-004
WEB_FEATURE=web/features/bio-crd-004
WEB_TESTS=tests/frontend/unit
E2E_TESTS=tests/frontend/playwright

# --- AC01: default ID-card barcode profile is alphanumeric ---
check "AC01 backend catalogue names Alphanumeric as the default profile" \
  bash -c "grep -q 'DefaultProfile = BarcodeProfile.Alphanumeric' $API_FEATURE/Domain/BarcodeProfileCatalog.cs"
check "AC01 frontend catalogue mirrors the same default" \
  bash -c "grep -q \"DEFAULT_BARCODE_PROFILE: BarcodeProfile = 'alphanumeric'\" $WEB_FEATURE/barcodeObjects.ts"
check "AC01 a newly-created barcode element starts on the default profile" \
  bash -c "grep -q 'profile: BarcodeProfile = DEFAULT_BARCODE_PROFILE' $WEB_FEATURE/barcodeObjects.ts"
check "AC01 a real-browser test proves the default toolbar button creates an alphanumeric barcode" \
  bash -c "grep -q 'BIO-CRD-004-AC01' $E2E_TESTS/bio-crd-004-card-objects.spec.ts"

# --- AC02: QR/PDF417/Data Matrix/Aztec are optional profiles, not default ---
check "AC02 backend catalogue names exactly the four optional 2D symbologies" bash -c \
  "grep -q 'BarcodeProfile.Qr,' $API_FEATURE/Domain/BarcodeProfileCatalog.cs &&
   grep -q 'BarcodeProfile.Pdf417,' $API_FEATURE/Domain/BarcodeProfileCatalog.cs &&
   grep -q 'BarcodeProfile.DataMatrix,' $API_FEATURE/Domain/BarcodeProfileCatalog.cs &&
   grep -q 'BarcodeProfile.Aztec,' $API_FEATURE/Domain/BarcodeProfileCatalog.cs"
check "AC02 frontend catalogue mirrors the same four optional profiles" \
  bash -c "grep -q \"OPTIONAL_BARCODE_PROFILES.*=.*\['qr', 'pdf417', 'dataMatrix', 'aztec'\]\" $WEB_FEATURE/barcodeObjects.ts"
check "AC02 IsDefault is false for every optional profile, true only for the default" \
  bash -c "grep -q 'IsDefault(BarcodeProfile profile) => profile == DefaultProfile' $API_FEATURE/Domain/BarcodeProfileCatalog.cs"
check "AC02 a real-browser test proves each optional profile requires its own explicit button" \
  bash -c "grep -q 'BIO-CRD-004-AC02' $E2E_TESTS/bio-crd-004-card-objects.spec.ts"

# --- AC03: barcode objects validate quiet zone/size/contrast ---
check "AC03 backend validator checks quiet zone, size and contrast" bash -c \
  "grep -q 'MinQuietZoneMm' $API_FEATURE/Domain/BarcodeValidation.cs &&
   grep -q 'MinDimensionMm' $API_FEATURE/Domain/BarcodeValidation.cs &&
   grep -q 'MaxDimensionMm' $API_FEATURE/Domain/BarcodeValidation.cs &&
   grep -q 'MinContrastRatio' $API_FEATURE/Domain/BarcodeValidation.cs"
check "AC03 frontend validator mirrors the same three checks and thresholds" bash -c \
  "grep -q 'MIN_QUIET_ZONE_MM' $WEB_FEATURE/barcodeObjects.ts &&
   grep -q 'MIN_DIMENSION_MM' $WEB_FEATURE/barcodeObjects.ts &&
   grep -q 'MAX_DIMENSION_MM' $WEB_FEATURE/barcodeObjects.ts &&
   grep -q 'MIN_CONTRAST_RATIO' $WEB_FEATURE/barcodeObjects.ts"
check "AC03 validation never throws - an invalid configuration is data, never an HTTP error" \
  bash -c "grep -q 'Validation never throws' $API_FEATURE/Domain/BarcodeValidation.cs"
check "AC03 contrast is computed via the WCAG relative-luminance formula, disclosed as a borrowed proxy" \
  bash -c "grep -q 'RelativeLuminance' $API_FEATURE/Domain/BarcodeValidation.cs && grep -q 'relativeLuminance' $WEB_FEATURE/barcodeObjects.ts"
check "AC03 a real-browser test proves quiet-zone and contrast violations surface and clear live" \
  bash -c "grep -q 'BIO-CRD-004-AC03' $E2E_TESTS/bio-crd-004-card-objects.spec.ts"

# --- AC04: signature assets are versioned ---
check "AC04 SignatureAsset is immutable/append-only per ADR-0001 §3.3 (no mutable version field, internal constructor)" \
  bash -c "grep -q 'internal SignatureAsset(' $API_FEATURE/Domain/SignatureAsset.cs && ! grep -q 'public.*set;' $API_FEATURE/Domain/SignatureAsset.cs"
check "AC04 content is defensively copied in and out (BIO-PHO-001-QA-01's lesson, applied from the start)" \
  bash -c "grep -c '(byte\[\])' $API_FEATURE/Domain/SignatureAsset.cs | grep -qE '^[2-9]$|^[1-9][0-9]+$'"
check "AC04 the repository assigns versions starting at 1 and never overwrites an existing version" \
  bash -c "grep -q 'nextVersion = (latest?.Version ?? 0) + 1' $API_FEATURE/Domain/ISignatureAssetRepository.cs"
check "AC04 versioning is tenant-scoped (every repository method takes tenantId)" \
  bash -c "grep -q 'GetLatestVersion(string tenantId, string lineageId)' $API_FEATURE/Domain/ISignatureAssetRepository.cs"
check "AC04 the frontend element pins a specific (lineageId, version) pair, never 'always latest'" \
  bash -c "grep -q 'readonly lineageId: string' $WEB_FEATURE/signatureObjects.ts && grep -q 'readonly version: number' $WEB_FEATURE/signatureObjects.ts"
check "AC04 a real-browser test proves a signature's version is pinned at creation and repointable without changing its lineage" \
  bash -c "grep -q 'BIO-CRD-004-AC04' $E2E_TESTS/bio-crd-004-card-objects.spec.ts"

# --- AC05: conditional visibility supports profile/card class rules ---
check "AC05 visibility rule supports both a card-class allowlist and a profile allowlist" \
  bash -c "grep -q 'readonly cardClasses' $WEB_FEATURE/visibility.ts && grep -q 'readonly profiles' $WEB_FEATURE/visibility.ts"
check "AC05 null on either list means unrestricted, not match-nothing (isVisible combines with AND)" \
  bash -c "grep -q 'rule.cardClasses === null' $WEB_FEATURE/visibility.ts && grep -q 'rule.profiles === null' $WEB_FEATURE/visibility.ts"
check "AC05 every one of this story's own card objects (barcode, signature, logo) carries a visibility rule" bash -c \
  "grep -q 'readonly visibilityRule: VisibilityRule' $WEB_FEATURE/barcodeObjects.ts &&
   grep -q 'readonly visibilityRule: VisibilityRule' $WEB_FEATURE/signatureObjects.ts &&
   grep -q 'readonly visibilityRule: VisibilityRule' $WEB_FEATURE/logoObjects.ts"
check "AC05 a real-browser test proves both dimensions actually hide/show an object under different preview contexts" \
  bash -c "grep -q 'BIO-CRD-004-AC05' $E2E_TESTS/bio-crd-004-card-objects.spec.ts"

# --- Module wiring and write-scope discipline ---
check "generic element ops are an independent copy, not imported from BIO-CRD-002/003" \
  bash -c "! grep -qE \"from '\.\./bio-crd-00[23]\" $WEB_FEATURE/canvasElementOps.ts"
check "the only reuse of BIO-CRD-002 is a read-only import of its exported pure format helpers" \
  bash -c "grep -q \"from '../bio-crd-002/cardFormats'\" $WEB_FEATURE/CardObjectsApp.tsx"
check "backend module lives one-way in api/Biolog.Api/Modules, not back in the feature project" \
  bash -c "test -f api/Biolog.Api/Modules/BioCrd004Module.cs && ! test -f '$API_FEATURE/BioCrd004Module.cs'"
check "registered in the packaged Program.cs module array" \
  bash -c "grep -q 'new BioCrd004Module()' api/Biolog.Api/Program.cs"
check "no ProjectReference to any other feature project" \
  bash -c "! grep -q '<ProjectReference' $API_FEATURE/Biolog.Features.CardObjects.csproj"
check "no canonical id of its own kind minted for a signature asset (ADR-0001: exactly six registered types)" \
  bash -c "! grep -qE 'sig_|SignaturePrefix' $API_FEATURE/Domain/SignatureAsset.cs"
check "the shared /card-designer route composes all three card-designer tools" \
  bash -c "grep -q 'CardDesignerApp' web/app/card-designer/page.tsx && grep -q 'CardTypographyApp' web/app/card-designer/page.tsx && grep -q 'CardObjectsApp' web/app/card-designer/page.tsx"

# --- Developer test wiring ---
check "backend unit/integration tests exist" bash -c \
  'test -f tests/features/bio-crd-004/BarcodeProfileCatalogTests.cs &&
   test -f tests/features/bio-crd-004/BarcodeValidationTests.cs &&
   test -f tests/features/bio-crd-004/SignatureAssetRepositoryTests.cs &&
   test -f tests/features/bio-crd-004/BarcodeProfileEndpointsIntegrationTests.cs &&
   test -f tests/features/bio-crd-004/SignatureAssetEndpointsIntegrationTests.cs'
check "a test proves cross-tenant signature isolation over real HTTP" \
  bash -c "grep -q 'invisible_and_unresumable_from_another_tenant' $API_TESTS/SignatureAssetEndpointsIntegrationTests.cs"
check "a test proves concurrent appends to the same lineage never collide or skip a version" \
  bash -c "grep -q 'Concurrent_appends' $API_TESTS/SignatureAssetRepositoryTests.cs"
check "frontend pure-logic unit tests exist for every model file" bash -c \
  'test -f tests/frontend/unit/bio-crd-004-barcode-objects.test.ts &&
   test -f tests/frontend/unit/bio-crd-004-signature-objects.test.ts &&
   test -f tests/frontend/unit/bio-crd-004-logo-objects.test.ts &&
   test -f tests/frontend/unit/bio-crd-004-visibility.test.ts &&
   test -f tests/frontend/unit/bio-crd-004-canvas-element-ops.test.ts'
check "a real-browser Playwright spec exists, including an accessibility baseline" bash -c \
  "test -f $E2E_TESTS/bio-crd-004-card-objects.spec.ts && grep -q 'axe' $E2E_TESTS/bio-crd-004-card-objects.spec.ts"
check "builder evidence recorded" test -d evidence/BIO-CRD-004/builder

echo "=============================="
echo "$pass passed, $fail failed"
[ "$fail" -eq 0 ]
