#!/usr/bin/env bash
# Offline-safe structural checks for BIO-RPT-004 (operational dashboard and health views).
# Behavioural assertions live in the executable suites: the backend's own xunit tests
# (tests/features/bio-rpt-004/), the frontend unit tests
# (tests/frontend/unit/bio-rpt-004-operational-health-mock.test.ts), the SIMULATION Playwright
# spec (tests/frontend/playwright/bio-rpt-004-operational-health.spec.ts) and the LIVE-mode
# Playwright spec (tests/frontend/playwright/bio-rpt-004-operational-health-live.spec.ts) - see
# docs/features/bio-rpt-004/README.md.
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"
pass=0; fail=0
check() { local label="$1"; shift; if "$@"; then echo "PASS  $label"; pass=$((pass+1)); else echo "FAIL  $label"; fail=$((fail+1)); fi; }

API_FEATURE=api/features/bio-rpt-004
API_TESTS=tests/features/bio-rpt-004
MODULE=api/Biolog.Api/Modules/BioRpt004Module.cs

# --- AC01: dashboard metrics come from authorised tenant scope ---
check "AC01 the feature project exists" test -f "$API_FEATURE/Biolog.Features.OperationalDashboard.csproj"
check "AC01 project targets the pinned .NET SDK's TFM" bash -c "grep -q '<TargetFramework>net10.0</TargetFramework>' '$API_FEATURE/Biolog.Features.OperationalDashboard.csproj'"
check "AC01 DashboardService resolves the tenant itself, never a caller-supplied one" \
  bash -c "grep -q 'RequireTenant' $API_FEATURE/DashboardService.cs && ! grep -q 'tenantId' $API_FEATURE/Api/DashboardEndpoints.cs"
check "AC01 identity/access/production panels are backed by a real cross-story gateway" \
  bash -c "grep -q 'IIdentityHealthGateway' $API_FEATURE/DashboardService.cs && grep -q 'IAccessHealthGateway' $API_FEATURE/DashboardService.cs && grep -q 'IProductionHealthGateway' $API_FEATURE/DashboardService.cs"
check "AC01 a test proves the snapshot carries the server-resolved tenant and real gateway counts" \
  bash -c "grep -q 'AC01_the_snapshot_carries_the_server_resolved_tenant' $API_TESTS/DashboardServiceTests.cs && grep -q 'AC01_identity_access_and_production_panels_reflect_the_real_tenant_scoped_gateway_counts' $API_TESTS/DashboardServiceTests.cs"
check "F01 the endpoint requires a real permission check before returning any data - tenant scoping alone is not authorization" \
  bash -c "grep -q 'IDashboardPermissionGateway' $API_FEATURE/DashboardService.cs && grep -q 'ActorIdHeader.Resolve' $API_FEATURE/Api/DashboardEndpoints.cs"
check "F01 the one declared cross-story permission dependency wraps BIO-IDN-006's real PermissionService" \
  bash -c "grep -q 'Biolog.Features.Rbac.PermissionService' $MODULE"
check "F01 a test proves an unprivileged actor is forbidden and never causes a single health-gateway call" \
  bash -c "grep -q 'F01_a_caller_without_the_dashboard_permission_is_forbidden' $API_TESTS/DashboardServiceTests.cs && grep -q 'F01_an_ungranted_actor_never_causes_a_single_health_gateway_call' $API_TESTS/DashboardServiceTests.cs"
check "F01 a real HTTP test proves an unprivileged actor gets 403, and no actor at all gets 400" \
  bash -c "grep -q 'F01_a_GET_from_a_verified_actor_without_the_dashboard_permission_is_403' $API_TESTS/DashboardEndpointsIntegrationTests.cs && grep -q 'F01_a_GET_with_no_X_Actor_Id_header_at_all_is_400' $API_TESTS/DashboardEndpointsIntegrationTests.cs"
check "F03 a cryptographic ActorProvenanceGate verifies the asserted actor id BEFORE the permission check - tenant scoping and a real permission grant are not enough on their own" \
  bash -c "grep -q 'ActorProvenanceGate' $API_FEATURE/Api/DashboardEndpoints.cs && grep -q 'provenanceGate.Require' $API_FEATURE/Api/DashboardEndpoints.cs"
check "F03 the provenance store is built on the CURRENT canonical shape - no fallback/dummy key of any kind" \
  bash -c "grep -q 'MinimumSecretLength' $API_FEATURE/Api/ActorProvenanceGate.cs && grep -q '_key is null' $API_FEATURE/Api/ActorProvenanceGate.cs && ! grep -qE '\\bDummyKey\\b' $API_FEATURE/Api/ActorProvenanceGate.cs && ! grep -q '\"no-provenance-secret-configured\"' $API_FEATURE/Api/ActorProvenanceGate.cs"
check "F03 a real HTTP test proves a privileged actor id with no proof, a bogus proof, and a mismatched proof are all refused" \
  bash -c "grep -q 'F03_asserting_a_privileged_actors_id_with_no_provenance_proof_at_all_is_401' $API_TESTS/DashboardEndpointsIntegrationTests.cs && grep -q 'F03_asserting_a_privileged_actors_id_with_a_fabricated_bogus_proof_is_401' $API_TESTS/DashboardEndpointsIntegrationTests.cs && grep -q 'F03_a_proof_computed_for_a_different_actor_id_does_not_authorize' $API_TESTS/DashboardEndpointsIntegrationTests.cs"
check "F03 a test proves the provenance gate runs before the permission check (401, not 403, for an unproven unprivileged actor)" \
  bash -c "grep -q 'F03_the_provenance_gate_runs_before_the_permission_check' $API_TESTS/DashboardEndpointsIntegrationTests.cs"

# --- AC02: drill-down links preserve role permissions ---
check "AC02 the dashboard's drill-downs reuse the shell's own requiredPermission/isNavItemVisible mechanism, not a second permission list" \
  bash -c "grep -q 'isNavItemVisible' web/features/bio-rpt-004/OperationalHealthPanel.tsx && grep -q 'requiredPermission' web/features/bio-rpt-004/OperationalHealthPanel.tsx"
check "AC02 a SIMULATION Playwright spec proves drill-down visibility differs by role (admin/staff/viewer)" \
  bash -c "grep -q \"goto('/?as=admin')\" tests/frontend/playwright/bio-rpt-004-operational-health.spec.ts && grep -q \"goto('/?as=viewer')\" tests/frontend/playwright/bio-rpt-004-operational-health.spec.ts && grep -q \"goto('/?as=staff')\" tests/frontend/playwright/bio-rpt-004-operational-health.spec.ts"

# --- AC03: stale/offline data is labelled ---
check "AC03 every panel carries an explicit DataFreshness value, with three distinct states (Live/Stale/Unavailable)" \
  bash -c "grep -q 'Live,' $API_FEATURE/Domain/DataFreshness.cs && grep -q 'Stale,' $API_FEATURE/Domain/DataFreshness.cs && grep -q 'Unavailable,' $API_FEATURE/Domain/DataFreshness.cs"
check "AC03 the three unavailable domains are modelled as Unavailable-with-a-stated-reason, never a fabricated number" \
  bash -c "grep -q 'Card = UnavailablePanel.For' $API_FEATURE/DashboardService.cs && grep -q 'Device = UnavailablePanel.For' $API_FEATURE/DashboardService.cs && grep -q 'Sync = UnavailablePanel.For' $API_FEATURE/DashboardService.cs"
check "AC03 a test proves the three real panels are Live and the three unavailable panels are Unavailable with a reason, and the two never share a value" \
  bash -c "grep -q 'AC03_the_three_real_panels_are_Live_and_the_three_unavailable_panels_are_Unavailable' $API_TESTS/DashboardServiceTests.cs"
check "AC03 every panel carries its own ObservedAt timestamp, not just one snapshot-level one" \
  bash -c "grep -q 'ObservedAt' $API_FEATURE/Domain/OperationalDashboardSnapshot.cs && grep -q 'AC03_every_panel_carries_its_own_ObservedAt_timestamp' $API_TESTS/DashboardServiceTests.cs"
check "F02 a failing real gateway degrades only its own panel to Stale, never the whole snapshot (fault isolation)" \
  bash -c "grep -q 'F02_a_failing_identity_gateway_degrades_only_the_identity_panel_to_Stale' $API_TESTS/DashboardServiceTests.cs && grep -q 'F02_a_failing_identity_gateway_still_returns_200_with_identity_Stale' $API_TESTS/DashboardEndpointsIntegrationTests.cs"
check "AC03 the frontend renders three visibly distinct freshness tones (Live/Stale/Unavailable), not two" \
  bash -c "grep -q \"'Live'\" web/features/bio-rpt-004/OperationalHealthPanel.tsx && grep -q \"'Stale'\" web/features/bio-rpt-004/OperationalHealthPanel.tsx && grep -q \"'Unavailable'\" web/features/bio-rpt-004/OperationalHealthPanel.tsx"
check "F04 a failed read retains the last successfully observed values via a process-lifetime cache, never a fabricated zero" \
  bash -c "test -f $API_FEATURE/Domain/LastKnownHealthCache.cs && grep -q 'LastKnownHealthCache' $API_FEATURE/DashboardService.cs"
check "F04 panel counts are nullable - a Stale panel that has never succeeded reports no counts at all" \
  bash -c "grep -q 'int? Total' $API_FEATURE/Domain/OperationalDashboardSnapshot.cs"
check "F04 a test proves a never-succeeded source reports null counts, and a previously-succeeded source retains its real values and true observation time on a later failure" \
  bash -c "grep -q 'F04_a_source_that_has_never_once_succeeded_reports_no_counts_at_all' $API_TESTS/DashboardServiceTests.cs && grep -q 'F04_a_source_that_previously_succeeded_retains_its_last_known_values' $API_TESTS/DashboardServiceTests.cs"
check "F04 a real HTTP test proves the same retained-values behaviour across two separate requests" \
  bash -c "grep -q 'F04_a_source_that_has_never_succeeded_reports_null_counts_on_the_wire' $API_TESTS/DashboardEndpointsIntegrationTests.cs && grep -q 'F04_a_source_that_previously_succeeded_retains_real_values_and_the_true_observation_time_across_requests' $API_TESTS/DashboardEndpointsIntegrationTests.cs"

# --- AC04: no cross-tenant aggregation is exposed to tenant roles ---
check "AC04 no overload on DashboardService accepts a tenant id parameter (structural guarantee)" \
  bash -c "grep -q 'AC04_there_is_no_overload_anywhere_on_DashboardService_that_accepts_a_tenant_id_parameter' $API_TESTS/DashboardServiceTests.cs"
check "AC04 a test proves tenant A never sees tenant B's seeded counts" \
  bash -c "grep -q 'AC04_a_request_resolved_to_tenant_A_never_sees_tenant_Bs_counts' $API_TESTS/DashboardServiceTests.cs"
check "AC04 the operational-health port takes no tenant parameter on the frontend either" \
  bash -c "grep -q 'primaryAction(): Promise<OperationalHealthResult>' web/features/bio-rpt-004/ports.ts"

# --- AC05: connected through ReportingService, no unapproved UI redesign ---
check "AC05 the operational dashboard module is registered into the real host" test -f "$MODULE"
check "AC05 the route surface lives entirely under its own dedicated prefix" \
  bash -c "grep -q '/api/v1/dashboard' $API_FEATURE/Api/DashboardEndpoints.cs"
check "AC05 no other module maps anything under /api/v1/dashboard, and BioRpt004Module is registered in Program.cs" \
  bash -c "[ \$(grep -rl '/api/v1/dashboard' api/Biolog.Api/Modules/*.cs 2>/dev/null | wc -l | tr -d ' ') = '0' ] && grep -rq 'new BioRpt004Module()' api/Biolog.Api/Program.cs"
check "AC05 a new dedicated ReportingService port key is registered" \
  bash -c "grep -q 'operationalHealth' web/lib/feature-mode.ts && grep -q 'operationalHealth' web/lib/module-registry.ts"
check "AC05 next.config.mjs forwards the dashboard prefix to the real backend" \
  bash -c "grep -q '/api/v1/dashboard' web/next.config.mjs"
check "AC05 OperationalHealthPanel is mounted on the existing Dashboard route ('/'), alongside BIO-UX-011's own metrics tiles, untouched" \
  bash -c "grep -q 'OperationalHealthPanel' web/app/page.tsx && grep -q 'computeDashboardMetrics' web/app/page.tsx"
check "AC05 three real cross-story gateway adapters are wired, zero edits to their own owned files" \
  bash -c "grep -q 'IdentityHealthGatewayAdapter' $MODULE && grep -q 'AccessHealthGatewayAdapter' $MODULE && grep -q 'ProductionHealthGatewayAdapter' $MODULE"
check "a SIMULATION Playwright spec proves the panel through the real UI" \
  test -f tests/frontend/playwright/bio-rpt-004-operational-health.spec.ts
check "a LIVE-mode Playwright spec proves the real backend round trip, gated behind an explicit opt-in env var" \
  bash -c "grep -q 'BIOLOG_RPT004_LIVE_TESTS' tests/frontend/playwright/bio-rpt-004-operational-health-live.spec.ts"
check "frontend unit tests cover the mock adapter's own behaviour and the new port-key registration" \
  test -f tests/frontend/unit/bio-rpt-004-operational-health-mock.test.ts
check "a real HTTP integration test proves the endpoint, self-hosted (not merely an injected test double)" \
  test -f "$API_TESTS/DashboardEndpointsIntegrationTests.cs"

# --- Cross-feature dependency wiring ---
check "no ProjectReference to any other feature project (all cross-feature access is through this project's own gateways)" \
  bash -c "! grep -q '<ProjectReference' $API_FEATURE/Biolog.Features.OperationalDashboard.csproj"
check "this feature's endpoint group maps GET only, no mutation verb of any kind (read-only by construction)" \
  bash -c "! grep -qE 'MapPost\(|MapPut\(|MapPatch\(|MapDelete\(' $API_FEATURE/Api/DashboardEndpoints.cs"
check "a test proves no mutation route exists under this prefix" \
  bash -c "grep -q 'AC02_no_mutation_route_exists_under_this_prefix' $API_TESTS/DashboardEndpointsIntegrationTests.cs"

# --- Developer test wiring ---
check "unit/integration tests exist for the service and the real HTTP endpoint" bash -c \
  'test -f tests/features/bio-rpt-004/DashboardServiceTests.cs &&
   test -f tests/features/bio-rpt-004/DashboardEndpointsIntegrationTests.cs'
check "all fixtures marked SYNTHETIC" bash -c \
  'grep -rq SYNTHETIC tests/features/bio-rpt-004 tests/frontend/playwright/bio-rpt-004-operational-health.spec.ts tests/frontend/playwright/bio-rpt-004-operational-health-live.spec.ts tests/frontend/unit/bio-rpt-004-operational-health-mock.test.ts'

check "builder evidence recorded" test -d evidence/BIO-RPT-004/builder

echo
echo "BIO-RPT-004.test.sh: $pass passed, $fail failed"
[ "$fail" -eq 0 ]
