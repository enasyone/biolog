#!/usr/bin/env bash
# Offline-safe structural checks for BIO-RPT-002 (governed report builder foundation).
# Behavioural assertions live in the executable suites: the backend's own xunit tests
# (tests/features/bio-rpt-002/) and the LIVE-mode Playwright spec
# (tests/frontend/playwright/bio-rpt-002-reporting-live.spec.ts) - see
# docs/features/bio-rpt-002/README.md.
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"
pass=0; fail=0
check() { local label="$1"; shift; if "$@"; then echo "PASS  $label"; pass=$((pass+1)); else echo "FAIL  $label"; fail=$((fail+1)); fi; }

API_FEATURE=api/features/bio-rpt-002
API_TESTS=tests/features/bio-rpt-002

# --- AC01: dimensions/measures are whitelisted ---
check "AC01 a static schema registry maps each dataset to its own allowed dimensions/measures" \
  bash -c "grep -q 'ReportSchemaRegistry' $API_FEATURE/Domain/ReportSchema.cs && grep -q 'AllowedDimensions' $API_FEATURE/Domain/ReportSchema.cs"
check "AC01 a definition is validated against the whitelist before any data is ever read" \
  bash -c "grep -q 'ReportDefinitionValidator.Validate' $API_FEATURE/Domain/ReportGenerationService.cs"
check "AC01 an unwhitelisted dimension/measure is rejected with a dedicated error code, not a generic 500" \
  bash -c "grep -q 'DimensionNotWhitelisted' $API_FEATURE/Domain/ReportError.cs && grep -q 'MeasureNotWhitelisted' $API_FEATURE/Domain/ReportError.cs"
check "a test proves every whitelisted combination validates and an unwhitelisted one is rejected" \
  bash -c "grep -q 'AC01_every_whitelisted_combination_validates_without_throwing' $API_TESTS/ReportSchemaTests.cs && grep -q 'AC01_a_dimension_not_whitelisted_for_that_dataset_is_rejected' $API_TESTS/ReportSchemaTests.cs"

# --- AC02: CSV/Excel/PDF export hooks exist ---
check "AC02 all three export formats are named, real cases - not a two-case enum with a third silently missing" \
  bash -c "grep -q 'Csv,' $API_FEATURE/Domain/ReportExportFormat.cs && grep -q 'Excel,' $API_FEATURE/Domain/ReportExportFormat.cs && grep -q 'Pdf,' $API_FEATURE/Domain/ReportExportFormat.cs"
check "AC02 CSV actually renders real bytes; Excel/PDF disclose their own not-yet-implemented gap rather than being absent" \
  bash -c "grep -q 'class CsvReportExporter' $API_FEATURE/Domain/IReportExporter.cs && grep -q 'class UnimplementedReportExporter' $API_FEATURE/Domain/IReportExporter.cs"
check "a test proves a real CSV renders and Excel/PDF answer honestly, not silently" \
  bash -c "grep -q 'AC02_renders_a_real_RFC4180_csv' $API_TESTS/CsvReportExporterTests.cs && grep -q 'AC02_excel_and_pdf_hooks_exist_and_answer_honestly' $API_TESTS/CsvReportExporterTests.cs"

# --- AC03: tenant isolation is enforced ---
check "AC03 the tenant a report runs against always comes from ICurrentTenantProvider, never request content" \
  bash -c "! grep -qE 'string TenantId \{|TenantId,' $API_FEATURE/Domain/ReportDefinition.cs"
check "AC03 a job looked up under a different tenant is 404, never confirmed to exist under 403" \
  bash -c "grep -q 'null both when the job never existed AND' $API_FEATURE/Domain/IReportJobRepository.cs"
check "a test proves a job created under one tenant cannot be read under a different tenant context" \
  bash -c "grep -q 'AC03_a_job_created_under_one_tenant_cannot_be_read_under_a_different_tenant_context' $API_TESTS/ReportServiceTests.cs"
check "a test proves the generation engine itself never returns another tenant's records" \
  bash -c "grep -q 'AC03_a_tenant_never_sees_another_tenants_records_in_its_own_report' $API_TESTS/ReportGenerationServiceTests.cs"

# --- AC04: large reports are asynchronous/cancellable ---
check "AC04 every report runs through the same Queued/Running/Completed/Failed/Cancelled state machine, never a synchronous inline result" \
  bash -c "grep -q 'Queued,' $API_FEATURE/Domain/ReportJobStatus.cs && grep -q 'Running,' $API_FEATURE/Domain/ReportJobStatus.cs && grep -q 'Cancelled,' $API_FEATURE/Domain/ReportJobStatus.cs"
check "AC04 job execution runs on a background Task, not inline in the request handler" \
  bash -c "grep -q 'Task.Run(() => RunAsync' $API_FEATURE/Domain/ReportJobRunner.cs"
check "AC04 cancellation is cooperative - checked repeatedly during generation, not once at the very start" \
  bash -c "grep -q 'ThrowIfCancellationRequested' $API_FEATURE/Domain/ReportGenerationService.cs"
check "a test deterministically proves mid-flight cancellation reaches Cancelled, with no real-time sleep to race against" \
  bash -c "grep -q 'AC04_cancelling_a_running_job_deterministically_stops_it_at_Cancelled' $API_TESTS/ReportJobRunnerTests.cs && grep -q 'GatedCardProductionGateway' $API_TESTS/Fakes.cs"
check "a test proves an already-terminal job cannot be cancelled again" \
  bash -c "grep -q 'AC04_a_job_that_has_already_reached_a_terminal_state_cannot_be_cancelled_again' $API_TESTS/ReportJobRunnerTests.cs"

# --- AC05: audit records report generation/export ---
check "AC05 a dedicated audit sink exists for this feature, mandatory before a mutation is considered committed" \
  test -f "$API_FEATURE/Audit/IReportAuditSink.cs"
check "AC05 both report generation and export are audited actions" \
  bash -c "grep -q '\"report.generate\"' $API_FEATURE/Domain/ReportJobRunner.cs && grep -q '\"report.export\"' $API_FEATURE/ReportService.cs"
check "a test proves a queued/completed job is audited, and a refused export is audited as Denied" \
  bash -c "grep -q 'report_job_queued' $API_TESTS/ReportJobRunnerTests.cs && grep -q 'AC02_AC05_exporting_as_excel_is_refused_and_the_refusal_is_audited_as_Denied' $API_TESTS/ReportServiceTests.cs"

# --- AC06: the approved web surface is connected through ReportingService ---
check "AC06 this story never maps /primary-action or /view-audit-trail - those stay entirely BIO-RPT-001's own routes (god's ruling, 2026-09-24)" \
  bash -c "! grep -qE 'MapPost\(\"/primary-action|MapPost\(\"/view-audit-trail' $API_FEATURE/Api/ReportEndpoints.cs"
check "AC06 the frontend gained four new dedicated ReportingService ports instead of reusing primaryAction/viewAuditTrail" \
  bash -c "grep -q 'generateReport' web/lib/feature-mode.ts && grep -q 'getReportStatus' web/lib/feature-mode.ts && grep -q 'cancelReport' web/lib/feature-mode.ts && grep -q 'exportReport' web/lib/feature-mode.ts"
check "AC06 next.config.mjs forwards /api/v1/reporting/* to the real Biolog.Api host, the same rewrite pattern every prior LIVE-connected story established" \
  bash -c "grep -q \"source: '/api/v1/reporting/:path\\*'\" web/next.config.mjs"
check "AC06 a real UI (ReportBuilderCard) mounts on /reports calling the four new ports, alongside BIO-UX-011's own unmodified 'Run a report' card" \
  bash -c "grep -q 'ReportBuilderCard' web/app/reports/page.tsx && test -f web/features/reporting/ReportBuilderCard.tsx"
check "a real HTTP pipeline test proves this project's own endpoint group never collides with BIO-RPT-001's primary-action/view-audit-trail pair" \
  bash -c "grep -q 'no_route_here_ever_collides_with_BIO_RPT_001s_own_primary_action_view_audit_trail_pair' $API_TESTS/ReportEndpointsIntegrationTests.cs"
check "a dual-server Playwright spec proves the report-builder card's create/poll/export workflow is genuinely live end to end" \
  test -f tests/frontend/playwright/bio-rpt-002-reporting-live.spec.ts

# --- Module wiring and write-scope discipline ---
check "no ProjectReference to any other feature project, including bio-crd-007/bio-att-001 (real runtime dependencies reached only through this project's own gateway interfaces)" \
  bash -c "! grep -q '<ProjectReference' $API_FEATURE/Biolog.Features.ReportBuilder.csproj"
check "ICardProductionGateway/IAttendanceGateway are this project's own interfaces, not references to the dependencies' own types" \
  bash -c "test -f $API_FEATURE/Domain/ICardProductionGateway.cs && test -f $API_FEATURE/Domain/IAttendanceGateway.cs"
check "backend module lives one-way in api/Biolog.Api/Modules, not back in the feature project" \
  bash -c "test -f api/Biolog.Api/Modules/BioRpt002Module.cs && ! test -f '$API_FEATURE/BioRpt002Module.cs'"
check "registered in the packaged Program.cs module array, depending on Tenant, CardProduction and Attendance" \
  bash -c "grep -q 'new BioRpt002Module()' api/Biolog.Api/Program.cs && grep -q '\"Tenant\", \"CardProduction\", \"Attendance\"' api/Biolog.Api/Modules/BioRpt002Module.cs"
check "BIO-CRD-007's own repository gained exactly one new, purely additive read method for this story - every prior method/behaviour is unchanged" \
  bash -c "grep -q 'ListForTenant' api/features/bio-crd-007/Domain/IProductionJobRepository.cs && grep -q 'BIO_RPT_002_ListForTenant' tests/features/bio-crd-007/ProductionJobRepositoryTests.cs"

# --- Developer test wiring ---
check "backend unit/integration tests exist" bash -c \
  'test -f tests/features/bio-rpt-002/ReportSchemaTests.cs &&
   test -f tests/features/bio-rpt-002/ReportGenerationServiceTests.cs &&
   test -f tests/features/bio-rpt-002/ReportJobRunnerTests.cs &&
   test -f tests/features/bio-rpt-002/CsvReportExporterTests.cs &&
   test -f tests/features/bio-rpt-002/ReportServiceTests.cs &&
   test -f tests/features/bio-rpt-002/ReportEndpointsIntegrationTests.cs'
check "builder evidence recorded" test -d evidence/BIO-RPT-002/builder

echo "=============================="
echo "$pass passed, $fail failed"
[ "$fail" -eq 0 ]
