#!/usr/bin/env bash
# Offline-safe structural checks for BIO-TRN-001 (routes, buses and person-assignment model).
# Behavioural assertions live in the executable xunit suites under tests/features/bio-trn-001/
# and tests/dotnet/Biolog.IntegrationTests/TransportHostChannelTests.cs (see
# docs/features/bio-trn-001/README.md).
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"
pass=0; fail=0
check() { local label="$1"; shift; if "$@"; then echo "PASS  $label"; pass=$((pass+1)); else echo "FAIL  $label"; fail=$((fail+1)); fi; }

FEATURE=api/features/bio-trn-001

# --- Project shape ---
check "feature project exists" test -f "$FEATURE/Biolog.Features.Transport.csproj"
check "project targets the pinned .NET SDK's TFM" bash -c "grep -q '<TargetFramework>net10.0</TargetFramework>' '$FEATURE/Biolog.Features.Transport.csproj'"
check "module lives one-way in api/Biolog.Api/Modules, not back in the feature project" \
  bash -c "test -f api/Biolog.Api/Modules/BioTrn001Module.cs && ! test -f '$FEATURE/BioTrn001Module.cs'"
check "registered in the packaged Program.cs module array" \
  bash -c "grep -q 'new BioTrn001Module()' api/Biolog.Api/Program.cs"
check "Biolog.Api.csproj references the feature project" \
  bash -c "grep -q 'features/bio-trn-001/Biolog.Features.Transport.csproj' api/Biolog.Api/Biolog.Api.csproj"
check "no ProjectReference from the feature project to any other feature project or Biolog.Api" \
  bash -c "! grep -q '<ProjectReference' '$FEATURE/Biolog.Features.Transport.csproj'"

# --- AC01: Route/vehicle/stop lifecycle is versioned ---
check "AC01 Route/Vehicle/Stop each carry an int Version and a CAS repository" \
  bash -c "grep -q 'required int Version' '$FEATURE/Domain/Route.cs' && grep -q 'required int Version' '$FEATURE/Domain/Vehicle.cs' && grep -q 'required int Version' '$FEATURE/Domain/Stop.cs'"
check "AC01 Route status is Draft/Active/Retired, one-way, dual-boundary validated" \
  bash -c "grep -q 'Draft,' '$FEATURE/Domain/TransportEnums.cs' && grep -q 'IsAllowedRouteTransition' '$FEATURE/Domain/TransportTransitions.cs' && grep -q 'IsAllowedRouteTransition' '$FEATURE/Domain/InMemoryRouteRepository.cs'"
check "AC01 Vehicle status cycles Active/OutOfService but Retired is terminal, dual-boundary validated" \
  bash -c "grep -q 'IsAllowedVehicleTransition' '$FEATURE/Domain/TransportTransitions.cs' && grep -q 'IsAllowedVehicleTransition' '$FEATURE/Domain/InMemoryVehicleRepository.cs'"
check "AC01 Stop status is Active/Retired, dual-boundary validated" \
  bash -c "grep -q 'IsAllowedStopTransition' '$FEATURE/Domain/TransportTransitions.cs' && grep -q 'IsAllowedStopTransition' '$FEATURE/Domain/InMemoryStopRepository.cs'"
check "AC01 no canonical identifier-registry prefix is minted for any of the four aggregates (disclosed, precedented boundary)" \
  bash -c "grep -q 'NewLocalId' '$FEATURE/Identifiers/CanonicalId.cs' && ! grep -qE '\"(rte|veh|stp|asn)_' '$FEATURE/Identifiers/CanonicalId.cs'"
check "AC01 a test proves a route activates exactly once and a failed re-activate leaves the record untouched" \
  bash -c "grep -q 'AC01_c_a_route_is_created_draft_and_activates_exactly_once' tests/features/bio-trn-001/RegistryServiceTests.cs"

# --- AC02: person assignments support validity windows ---
check "AC02 PersonAssignment carries ValidFrom/nullable ValidUntil and a Version" \
  bash -c "grep -q 'required string ValidFrom' '$FEATURE/Domain/PersonAssignment.cs' && grep -q 'string? ValidUntil' '$FEATURE/Domain/PersonAssignment.cs'"
check "AC02 assignment create/end are gated by permission AND a verified ADR-0006 provenance proof" \
  bash -c "grep -q 'AssignPermission' '$FEATURE/PersonAssignmentService.cs' && grep -q 'provenanceGate.Require' '$FEATURE/Api/TransportEndpoints.cs'"
check "AC02 the overlap invariant is protected by its own tenant/person-scoped lock, acquired before the per-record lock" \
  bash -c "grep -q 'PersonAssignmentUniquenessLockRegistry' '$FEATURE/PersonAssignmentService.cs' && grep -q 'WindowsOverlap' '$FEATURE/PersonAssignmentService.cs'"
check "AC02 every assignment create/end transition is audited" \
  bash -c "grep -q 'transport.assignment_created' '$FEATURE/PersonAssignmentService.cs' && grep -q 'transport.assignment_ended' '$FEATURE/PersonAssignmentService.cs'"
check "AC02 a test proves an overlapping window for the same person is rejected" \
  bash -c "grep -q 'AC02_f_an_overlapping_window_for_the_same_person_is_rejected' tests/features/bio-trn-001/PersonAssignmentServiceTests.cs"
check "AC02 a test proves concurrent overlapping creates for the same person only let one win (race, not just sequential)" \
  bash -c "grep -q 'AC02_h_concurrent_create_attempts_for_the_same_person_and_overlapping_window_only_one_wins' tests/features/bio-trn-001/PersonAssignmentServiceTests.cs"

# --- AC03: vehicle/device linkage is supported ---
check "AC03 Vehicle carries a nullable DeviceId" \
  bash -c "grep -q 'string? DeviceId' '$FEATURE/Domain/Vehicle.cs'"
check "AC03 device linkage is existence-checked via a feature-owned gateway (BIO-IDN-002-QA-01's lesson), never status-gated" \
  bash -c "grep -q 'IDeviceExistenceGateway' '$FEATURE/VehicleService.cs' && grep -q 'DeviceNotFound' '$FEATURE/VehicleService.cs'"
check "AC03 the gateway is implemented by wrapping BIO-DEV-001's own DeviceService directly, not a ProjectReference" \
  bash -c "grep -q 'DeviceExistenceGatewayAdapter' api/Biolog.Api/Modules/BioTrn001Module.cs && grep -q 'DeviceRegistry.DeviceService' api/Biolog.Api/Modules/BioTrn001Module.cs"
check "AC03 a test proves a device_id that does not exist is rejected" \
  bash -c "grep -q 'AC03_a_a_device_id_that_does_not_exist_is_rejected' tests/features/bio-trn-001/RegistryServiceTests.cs"
check "AC03 a real-host test proves the gateway reaches the real BIO-DEV-001 device store" \
  bash -c "grep -q 'TRN001_AC03_creating_a_vehicle_with_a_real_device_id_through_the_real_host_reaches_the_real_BIO_DEV_001_gateway_and_succeeds' tests/dotnet/Biolog.IntegrationTests/TransportHostChannelTests.cs"

# --- AC04: tenant/campus boundaries apply ---
check "AC04 Route/Vehicle/Stop each carry a nullable, feature-local CampusId (no registered Campus entity exists)" \
  bash -c "grep -q 'string? CampusId' '$FEATURE/Domain/Route.cs' && grep -q 'string? CampusId' '$FEATURE/Domain/Vehicle.cs' && grep -q 'string? CampusId' '$FEATURE/Domain/Stop.cs'"
check "AC04 assignment creation enforces route/stop campus consistency when both declare one" \
  bash -c "grep -q 'CampusMismatch' '$FEATURE/PersonAssignmentService.cs'"
check "AC02 automatic expiry is a pure function, computed at read time, a distinct type from anything persisted" \
  bash -c "grep -q 'static class TransportStatusEvaluator' '$FEATURE/Domain/TransportStatusEvaluator.cs' && grep -q 'enum EffectiveAssignmentStatus' '$FEATURE/Domain/TransportEnums.cs' && ! grep -q 'Repository\|AuditSink' '$FEATURE/Domain/TransportStatusEvaluator.cs'"
check "AC02 a test proves the identical stored assignment classifies differently at two different read times" \
  bash -c "grep -q 'the_identical_stored_record_classifies_differently_at_two_different_read_times_proving_expiry_is_computed_not_stored' tests/features/bio-trn-001/TransportStatusEvaluatorTests.cs"
check "AC04 a test proves a campus mismatch between route and stop is rejected" \
  bash -c "grep -q 'AC04_a_a_campus_mismatch_between_route_and_stop_is_rejected' tests/features/bio-trn-001/PersonAssignmentServiceTests.cs"

# --- AC05: the approved web surface (/transport) is connected through TransportService to a live implementation ---
# BIO-TRN-002 (Tunde, same owner) took over /primary-action's own mapping/handler from this
# story's own TransportEndpoints.cs once boarding/alighting event capture existed to answer it
# for real - a disclosed, pre-anticipated handoff (this story's own README already named the
# placeholder as standing in for exactly that capability). This check is updated to match: only
# /assign-route is still this story's own route. See docs/features/bio-trn-002/README.md "Why
# this story edits BIO-TRN-001's own endpoint mapping" for the full record.
check "AC05 assign-route's live adapter endpoint is mapped for real (no route/verb change to web/features/transport/live.ts); primary-action now belongs to BIO-TRN-002" \
  bash -c "grep -q '\"/assign-route\"' '$FEATURE/Api/TransportEndpoints.cs' && ! grep -q '\"/primary-action\"' '$FEATURE/Api/TransportEndpoints.cs'"
check "AC05 web/features/transport/live.ts still calls the exact same two routes BIO-UX-005/010 originally scaffolded (no redesign, just connected)" \
  bash -c "grep -q \"'/api/v1/transport/primary-action'\" web/features/transport/live.ts && grep -q \"'/api/v1/transport/assign-route'\" web/features/transport/live.ts"
check "AC05 transport-page.tsx, ports.ts and mock.ts are untouched by this story (no unapproved UI redesign)" \
  bash -c "! grep -q 'BIO-TRN-001' web/features/transport/transport-page.tsx web/features/transport/ports.ts web/features/transport/mock.ts"
check "AC05 web/next.config.mjs proxies /api/v1/transport/* to the real backend" \
  bash -c "grep -q \"'/api/v1/transport/:path\\*'\" web/next.config.mjs"
check "AC05 the demo bridge exercises real domain route/stop/vehicle seeding and real assignment creation, not a canned string" \
  bash -c "grep -q 'RouteRecord.Create' '$FEATURE/Demo/TransportDemoBridgeService.cs' && grep -q 'PersonAssignment.Create' '$FEATURE/Demo/TransportDemoBridgeService.cs'"
check "AC05 a real-host test proves both bridge routes answer a genuinely headerless request" \
  bash -c "grep -q 'TRN001_AC05_the_two_UI_bridge_routes_answer_a_genuinely_headerless_request_through_the_real_host' tests/dotnet/Biolog.IntegrationTests/TransportHostChannelTests.cs"

echo
echo "BIO-TRN-001 structural checks: $pass passed, $fail failed"
[ "$fail" -eq 0 ]
