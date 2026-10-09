#!/usr/bin/env bash
# Offline-safe structural checks for BIO-CRD-008 (published card-template lifecycle and
# provenance). Behavioural assertions live in the executable suites: the backend's own xunit
# tests (tests/features/bio-crd-008/), the frontend unit tests
# (tests/frontend/unit/bio-crd-008-template-lifecycle-mock.test.ts), the SIMULATION Playwright
# spec (tests/frontend/playwright/bio-crd-008-template-lifecycle.spec.ts) and the LIVE-mode
# Playwright spec (tests/frontend/playwright/bio-crd-008-template-lifecycle-live.spec.ts) - see
# docs/features/bio-crd-008/README.md.
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"
pass=0; fail=0
check() { local label="$1"; shift; if "$@"; then echo "PASS  $label"; pass=$((pass+1)); else echo "FAIL  $label"; fail=$((fail+1)); fi; }

API_FEATURE=api/features/bio-crd-008
API_TESTS=tests/features/bio-crd-008
WEB_FEATURE=web/features/bio-crd-008

# --- AC01: published template is immutable ---
check "AC01 a published/retired template refuses in-place Edit" \
  bash -c "grep -q 'IsImmutable' $API_FEATURE/Domain/CardTemplate.cs && grep -q 'CardTemplateError.Immutable' $API_FEATURE/Domain/CardTemplate.cs"
check "a test proves a published template's Edit call throws Immutable, at 409" \
  bash -c "grep -q 'AC01_a_published_template_refuses_edit_in_place' $API_TESTS/CardTemplateLifecycleTests.cs"
check "an integration test proves the real HTTP PUT is refused with 409 after publish" \
  bash -c "grep -q 'AC01_full_lifecycle_draft_to_published_then_immutable' $API_TESTS/CardTemplateEndpointsIntegrationTests.cs"

# --- AC02: edit creates new version ---
check "AC02 the only way to change a published/retired template's content is a brand-new row (CreateNextDraftVersion)" \
  bash -c "grep -q 'CreateNextDraftVersion' $API_FEATURE/Domain/CardTemplate.cs"
check "a test proves the new version is a distinct row, same lineage, next version number, source untouched" \
  bash -c "grep -q 'AC02_a_new_version_from_a_published_template_is_a_brand_new_draft_row_the_source_untouched' $API_TESTS/CardTemplateLifecycleTests.cs"
check "a test proves publishing a new version auto-retires the lineage's previously published one" \
  bash -c "grep -q 'AC02_publishing_a_new_version_auto_retires_the_previously_published_one_only_one_published_row_per_lineage' $API_TESTS/CardTemplateServiceTests.cs"

# --- AC03: retains size/orientation, objects/fonts/watermarks/assets/photo spec/barcode/signature rules ---
check "AC03 the snapshot carries format/orientation, front/back elements, watermark, photo, barcode and signature" \
  bash -c "grep -q 'FormatKind' $API_FEATURE/Domain/CardTemplateSnapshot.cs && grep -q 'FrontElements' $API_FEATURE/Domain/CardTemplateSnapshot.cs && grep -q 'Watermark' $API_FEATURE/Domain/CardTemplateSnapshot.cs && grep -q 'Photo' $API_FEATURE/Domain/CardTemplateSnapshot.cs && grep -q 'Barcode' $API_FEATURE/Domain/CardTemplateSnapshot.cs && grep -q 'Signature' $API_FEATURE/Domain/CardTemplateSnapshot.cs"
check "AC03 a required signature rule is validated against a real, existing signature asset (the one genuine cross-story gateway)" \
  bash -c "grep -q 'ISignatureAssetGateway' $API_FEATURE/Domain/CardTemplateSnapshotValidator.cs"
check "a test proves the round trip retains every snapshot field exactly" \
  bash -c "grep -q 'AC03_creating_a_draft_retains_every_snapshot_field_exactly_on_round_trip' $API_TESTS/CardTemplateServiceTests.cs"
check "a test proves an unknown signature asset reference is rejected, not silently accepted" \
  bash -c "grep -q 'AC03_a_required_signature_rule_must_reference_a_real_existing_signature_asset' $API_TESTS/CardTemplateServiceTests.cs"

# --- AC04: production references exact published version ---
check "AC04 the one sanctioned source of the exact published version - never returns a Draft/Retired row" \
  bash -c "grep -q 'GetPublished' $API_FEATURE/CardTemplateService.cs"
check "AC04 a purely additive TemplateId/TemplateVersion pair exists on BIO-CRD-007's own ProductionJob" \
  bash -c "grep -q 'TemplateId' api/features/bio-crd-007/Domain/ProductionJob.cs && grep -q 'TemplateVersion' api/features/bio-crd-007/Domain/ProductionJob.cs"
check "AC04 CreateJobAsync's new template parameters are optional, defaulting to null - every existing call site unaffected" \
  bash -c "grep -q 'string? templateId = null, int? templateVersion = null' api/features/bio-crd-007/ProductionService.cs"
check "a regression test proves a job stamped with a template reference retains it, and omitting it leaves both fields null" \
  bash -c "grep -q 'CreateJob_optionally_records_the_exact_published_template_version' tests/features/bio-crd-007/ProductionJobLifecycleTests.cs && grep -q 'CreateJob_omitting_the_template_reference_leaves_both_fields_null' tests/features/bio-crd-007/ProductionJobLifecycleTests.cs"

# --- AC05: connected through CredentialService, no unapproved UI redesign ---
check "AC05 the card-template lifecycle module is registered into the real host" \
  test -f api/Biolog.Api/Modules/BioCrd008Module.cs
check "AC05 the route surface lives entirely under its own dedicated prefix" \
  bash -c "grep -q '/api/v1/card-templates' $API_FEATURE/Api/CardTemplateEndpoints.cs"
check "AC05 no other module maps anything under /api/v1/card-templates" \
  bash -c "[ \$(grep -rl 'MapGroup(\"/api/v1/card-templates\")' api/Biolog.Api/Modules/*.cs | wc -l | tr -d ' ') = '0' ] && grep -rq 'BioCrd008Module' api/Biolog.Api/Program.cs"
check "AC05 eight new dedicated CredentialService port keys are registered" \
  bash -c "grep -q 'createTemplateDraft' web/lib/feature-mode.ts && grep -q 'createNextTemplateVersion' web/lib/feature-mode.ts"
check "AC05 next.config.mjs forwards the card-templates prefix to the real backend" \
  bash -c "grep -q '/api/v1/card-templates' web/next.config.mjs"
check "AC05 TemplateLifecycleBar is mounted on /card-designer alongside the existing canvas and SaveDesignBar" \
  bash -c "grep -q 'TemplateLifecycleBar' web/app/card-designer/page.tsx && grep -q 'SaveDesignBar' web/app/card-designer/page.tsx"
check "AC05 the three canvas apps' own onChange lift is additive - each still has zero I/O of its own" \
  bash -c "! grep -q 'fetch(' web/features/bio-crd-002/CardDesignerApp.tsx && ! grep -q 'fetch(' web/features/bio-crd-003/CardTypographyApp.tsx && ! grep -q 'fetch(' web/features/bio-crd-004/CardObjectsApp.tsx"
check "a SIMULATION Playwright spec proves the full lifecycle through the real UI" \
  test -f tests/frontend/playwright/bio-crd-008-template-lifecycle.spec.ts
check "a LIVE-mode Playwright spec proves the real backend round trip, gated behind an explicit opt-in env var" \
  bash -c "grep -q 'BIOLOG_CRD008_LIVE_TESTS' tests/frontend/playwright/bio-crd-008-template-lifecycle-live.spec.ts"
check "frontend unit tests cover the mock adapters' own boundary behaviours" \
  test -f tests/frontend/unit/bio-crd-008-template-lifecycle-mock.test.ts

echo
echo "BIO-CRD-008.test.sh: $pass passed, $fail failed"
[ "$fail" -eq 0 ]
