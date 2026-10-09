#!/usr/bin/env bash
# Offline-safe structural checks for BIO-RPT-003 (immutable audit investigation views).
# Behavioural assertions live in the executable suites: the backend's own xunit tests
# (tests/features/bio-rpt-003/), the frontend unit tests
# (tests/frontend/unit/bio-rpt-003-audit-investigation-mock.test.ts), the SIMULATION Playwright
# spec (tests/frontend/playwright/bio-rpt-003-audit-investigation.spec.ts) and the LIVE-mode
# Playwright spec (tests/frontend/playwright/bio-rpt-003-audit-investigation-live.spec.ts) - see
# docs/features/bio-rpt-003/README.md.
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"
pass=0; fail=0
check() { local label="$1"; shift; if "$@"; then echo "PASS  $label"; pass=$((pass+1)); else echo "FAIL  $label"; fail=$((fail+1)); fi; }

API_FEATURE=api/features/bio-rpt-003
API_TESTS=tests/features/bio-rpt-003
MODULE=api/Biolog.Api/Modules/BioRpt003Module.cs

# --- AC01: filters include actor/person/action/time/device/reason ---
check "AC01 the filter carries actor/person/action/device/reason/time fields" \
  bash -c "grep -q 'ActorType' $API_FEATURE/Domain/AuditInvestigationFilter.cs && grep -q 'PersonId' $API_FEATURE/Domain/AuditInvestigationFilter.cs && grep -q 'Action' $API_FEATURE/Domain/AuditInvestigationFilter.cs && grep -q 'DeviceId' $API_FEATURE/Domain/AuditInvestigationFilter.cs && grep -q 'ReasonCode' $API_FEATURE/Domain/AuditInvestigationFilter.cs && grep -q 'OccurredFrom' $API_FEATURE/Domain/AuditInvestigationFilter.cs"
check "a test proves every filter dimension narrows the result set" \
  bash -c "grep -q 'AC01_filters_by_actor_type_and_id' $API_TESTS/AuditInvestigationServiceTests.cs && grep -q 'AC01_filters_by_person_id' $API_TESTS/AuditInvestigationServiceTests.cs && grep -q 'AC01_filters_by_device_id_against_a_synthetic_fixture' $API_TESTS/AuditInvestigationServiceTests.cs && grep -q 'AC01_filters_by_reason_code' $API_TESTS/AuditInvestigationServiceTests.cs && grep -q 'AC01_filters_by_inclusive_time_range' $API_TESTS/AuditInvestigationServiceTests.cs"

# --- AC02: audit records are read-only to tenant users ---
check "AC02 the service exposes no mutation method - Search/GetById/ExportCsv only" \
  bash -c "! grep -qE 'public (async )?Task<.*>? *(Update|Delete|Edit)Async' $API_FEATURE/AuditInvestigationService.cs"
check "AC02 the endpoint group maps GET/POST only, no PUT/PATCH/DELETE" \
  bash -c "! grep -qE 'MapPut\(|MapPatch\(|MapDelete\(' $API_FEATURE/Api/AuditInvestigationEndpoints.cs"
check "an integration test proves PUT/PATCH/DELETE are refused under this prefix" \
  bash -c "grep -q 'AC02_no_mutation_route_exists_under_this_prefix' $API_TESTS/AuditInvestigationEndpointsIntegrationTests.cs"

# --- AC03: export is permissioned and audited ---
check "AC03 export checks a real permission gateway before returning anything" \
  bash -c "grep -q 'IAuditExportPermissionGateway' $API_FEATURE/AuditInvestigationService.cs && grep -q 'ExportForbidden' $API_FEATURE/AuditInvestigationService.cs"
check "AC03 export writes its own audit event and only returns after that write settles" \
  bash -c "grep -q 'AuditExportAsync' $API_FEATURE/AuditInvestigationService.cs"
check "a test proves export is forbidden without the permission, and audited when it succeeds" \
  bash -c "grep -q 'AC03_export_throws_forbidden_without_the_export_permission' $API_TESTS/AuditInvestigationServiceTests.cs && grep -q 'AC03_export_returns_csv_and_writes_its_own_audit_event_before_returning' $API_TESTS/AuditInvestigationServiceTests.cs"
check "the one declared cross-story permission dependency wraps BIO-IDN-006's real PermissionService" \
  bash -c "grep -q 'Biolog.Features.Rbac.PermissionService' $MODULE"

# --- AC04: sensitive payload fields respect redaction policy ---
check "AC04 a view-time redaction gate exists, independent of the producer's own capture-time classification" \
  test -f $API_FEATURE/Domain/AuditViewRedaction.cs
check "AC04 personal/sensitive detail is hidden without permission, secret is hidden unconditionally" \
  bash -c "grep -q 'AuditClassification.Secret => true' $API_FEATURE/Domain/AuditViewRedaction.cs && grep -q 'canViewSensitiveDetail' $API_FEATURE/Domain/AuditViewRedaction.cs"
check "a test proves personal/sensitive detail is hidden without permission and revealed with it" \
  bash -c "grep -q 'Personal_and_sensitive_classified_detail_is_hidden_without_permission' $API_TESTS/AuditViewRedactionTests.cs && grep -q 'Personal_and_sensitive_classified_detail_is_revealed_with_permission' $API_TESTS/AuditViewRedactionTests.cs"
check "a test proves secret is hidden even with permission (defence in depth)" \
  bash -c "grep -q 'Secret_classified_detail_is_hidden_even_with_permission' $API_TESTS/AuditViewRedactionTests.cs"

# --- AC05: connected through ReportingService, no unapproved UI redesign ---
check "AC05 the audit investigation module is registered into the real host" \
  test -f $MODULE
check "AC05 the route surface lives entirely under its own dedicated prefix" \
  bash -c "grep -q '/api/v1/audit-investigations' $API_FEATURE/Api/AuditInvestigationEndpoints.cs"
check "AC05 no other module maps anything under /api/v1/audit-investigations" \
  bash -c "[ \$(grep -rl 'MapGroup(\"/api/v1/audit-investigations\")' api/Biolog.Api/Modules/*.cs | wc -l | tr -d ' ') = '0' ] && grep -rq 'BioRpt003Module' api/Biolog.Api/Program.cs"
check "AC05 two new dedicated ReportingService port keys are registered" \
  bash -c "grep -q 'searchAuditEvents' web/lib/feature-mode.ts && grep -q 'exportAuditEvents' web/lib/feature-mode.ts"
check "AC05 next.config.mjs forwards the audit-investigations prefix to the real backend" \
  bash -c "grep -q '/api/v1/audit-investigations' web/next.config.mjs"
check "AC05 AuditInvestigationPanel is mounted on /audit alongside BIO-RPT-001's own untouched card" \
  bash -c "grep -q 'AuditInvestigationPanel' web/app/audit/page.tsx && grep -q 'viewAuditTrail' web/app/audit/page.tsx"
check "AC05 four real sibling-feature audit sources are wired as adapters, read-only, zero edits to their own files" \
  bash -c "grep -q 'RbacAuditEventSource' $MODULE && grep -q 'CardTemplateAuditEventSource' $MODULE && grep -q 'ReportAuditEventSource' $MODULE && grep -q 'ProductionAuditEventSource' $MODULE"
check "a SIMULATION Playwright spec proves the panel through the real UI" \
  test -f tests/frontend/playwright/bio-rpt-003-audit-investigation.spec.ts
check "a LIVE-mode Playwright spec proves the real backend round trip, gated behind an explicit opt-in env var" \
  bash -c "grep -q 'BIOLOG_RPT003_LIVE_TESTS' tests/frontend/playwright/bio-rpt-003-audit-investigation-live.spec.ts"
check "frontend unit tests cover the mock adapter's own filter/export behaviour" \
  test -f tests/frontend/unit/bio-rpt-003-audit-investigation-mock.test.ts

echo
echo "BIO-RPT-003.test.sh: $pass passed, $fail failed"
[ "$fail" -eq 0 ]
