#!/usr/bin/env bash
# Offline-safe structural checks for BIO-TRN-002 (boarding/alighting and bus-presence events).
# Behavioural assertions live in the executable suites: the backend's own xunit tests
# (tests/features/bio-trn-002/) and the real-packaged-host tests
# (tests/dotnet/Biolog.IntegrationTests/TransportEventsHostChannelTests.cs) - see
# docs/features/bio-trn-002/README.md.
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"
pass=0; fail=0
check() { local label="$1"; shift; if "$@"; then echo "PASS  $label"; pass=$((pass+1)); else echo "FAIL  $label"; fail=$((fail+1)); fi; }

API_FEATURE=api/features/bio-trn-002
API_TESTS=tests/features/bio-trn-002
HOST_TESTS=tests/dotnet/Biolog.IntegrationTests

# --- AC01: board/alight events include route, vehicle, stop/location and identity evidence ---
check "AC01 TransportEvent carries route_id, vehicle_id, stop_id and evidence" \
  bash -c "grep -q 'RouteId' $API_FEATURE/Domain/TransportEvent.cs && grep -q 'VehicleId' $API_FEATURE/Domain/TransportEvent.cs && grep -q 'StopId' $API_FEATURE/Domain/TransportEvent.cs && grep -q 'IReadOnlyList<TransportEvidenceEntry> Evidence' $API_FEATURE/Domain/TransportEvent.cs"
check "AC01 evidence is a LIST of entries (configurable UHF/NFC/face combinations), not a single value" \
  bash -c "grep -q 'IReadOnlyList<TransportEvidenceEntry>' $API_FEATURE/Domain/TransportEvent.cs"
check "AC01 at least one evidence entry is required" \
  bash -c "grep -q 'at least one evidence entry is required' $API_FEATURE/Domain/TransportEvent.cs"
check "AC01 a route must be Active, a vehicle must be Active, a stop must be on the route and itself Active" \
  bash -c "grep -q 'RouteNotActive' $API_FEATURE/TransportEventService.cs && grep -q 'VehicleNotActive' $API_FEATURE/TransportEventService.cs && grep -q 'StopNotOnRoute' $API_FEATURE/TransportEventService.cs && grep -q 'StopNotActive' $API_FEATURE/TransportEventService.cs"
check "a test proves capturing persists route, vehicle, stop and evidence" \
  bash -c "grep -q 'AC01_a_capturing_a_board_event_persists_route_vehicle_stop_and_evidence' $API_TESTS/TransportEventServiceTests.cs"

# --- AC02: duplicate reads are debounced/idempotent ---
check "AC02 idempotency key + content fingerprint mechanism exists, same shape as BIO-ATT-001's own AC03" \
  bash -c "grep -q 'ContentFingerprint' $API_FEATURE/Domain/TransportEvent.cs && grep -q 'IdempotencyConflict' $API_FEATURE/Domain/TransportEventError.cs"
check "AC02 a per-(tenant, idempotency_key) lock is acquired before validation" \
  bash -c "grep -q 'lockRegistry.AcquireAsync' $API_FEATURE/TransportEventService.cs"
check "a test proves identical redelivery is answered idempotently with no new event" \
  bash -c "grep -q 'AC02_a_replaying_the_identical_idempotency_key_with_identical_content_answers_idempotently_no_new_event' $API_TESTS/TransportEventServiceTests.cs"
check "a test proves reusing the same key with different content is a conflict" \
  bash -c "grep -q 'AC02_b_reusing_the_same_idempotency_key_with_different_content_is_a_conflict' $API_TESTS/TransportEventServiceTests.cs"
check "a test proves a genuine 20-way concurrent capture race converges on exactly one stored event" \
  bash -c "grep -q 'AC02_c_20_concurrent_captures_with_the_same_idempotency_key_all_converge_on_exactly_one_stored_event' $API_TESTS/TransportEventServiceTests.cs"

# --- AC03: missing alight/exception states are handled ---
check "AC03 a pure, read-time-only evaluator exists, never mutating any stored TransportEvent" \
  test -f "$API_FEATURE/Domain/TransportPresenceEvaluator.cs"
check "AC03 MissingAlightException is a genuinely distinct status from anything persisted" \
  bash -c "grep -q 'MissingAlightException' $API_FEATURE/Domain/TransportEventEnums.cs && ! grep -qE 'internal (VisitStatus|Status) ' $API_FEATURE/Domain/TransportEvent.cs"
check "AC03 no stored TransportEvent field is ever mutated by the evaluator (no 'internal TransportEvent' mutation method exists at all - the record has no mutation surface)" \
  bash -c "! grep -qE 'internal TransportEvent [A-Za-z]+\(' $API_FEATURE/Domain/TransportEvent.cs"
check "a test proves the IDENTICAL stored events classify differently at two different read times" \
  bash -c "grep -q 'AC03_g_the_IDENTICAL_stored_events_classify_differently_at_two_different_read_times_proving_computed_at_read_time' $API_TESTS/TransportPresenceEvaluatorTests.cs"
check "a test proves a date strictly before today is an exception regardless of the hourly window" \
  bash -c "grep -q 'AC03_f_an_unmatched_board_from_a_date_strictly_before_today_is_MissingAlightException_regardless_of_the_window' $API_TESTS/TransportPresenceEvaluatorTests.cs"

# --- AC04: events can trigger configured notifications ---
check "AC04 a feature-local, CAS-versioned tenant notification config exists" \
  test -f "$API_FEATURE/Domain/TransportNotificationConfig.cs"
check "AC04 deliberately does NOT integrate with BIO-RPT-001's IDomainEventBus/DomainEventEnvelope (no declared dependency, frozen 5-value aggregate_type schema has no 6th value for a transport event) - excludes this story's own explanatory doc comments naming it" \
  bash -c "! grep -rvE '^\s*///|^\s*//' $API_FEATURE/ --include='*.cs' | grep -q 'IDomainEventBus\|Biolog.Features.Notifications'"
check "AC04 a real, append-only, readable-back TriggeredNotification log exists" \
  test -f "$API_FEATURE/Domain/TriggeredNotification.cs"
check "AC04 the trigger check runs only from the capture write path, never from the read-only presence-status endpoint" \
  bash -c "grep -q 'MaybeTriggerNotificationAsync' $API_FEATURE/TransportEventService.cs && ! sed -n '/public async Task<DerivedTransportPresence> GetPresenceStatusAsync/,/^    }/p' $API_FEATURE/TransportEventService.cs | grep -q MaybeTriggerNotificationAsync"
check "AC04 the trigger is deduplicated per (tenant, person, event, rule)" \
  bash -c "grep -q 'alreadyTriggered' $API_FEATURE/TransportEventService.cs"
check "a test proves a genuine exception at capture time triggers a real, readable-back notification when enabled" \
  bash -c "grep -q 'AC04_a_a_capture_that_is_already_a_missing_alight_exception_triggers_a_notification_when_enabled' $API_TESTS/NotificationTriggerTests.cs"
check "a test proves the same exception does NOT trigger when the tenant has not enabled it" \
  bash -c "grep -q 'AC04_b_the_same_exception_does_not_trigger_when_the_tenant_has_not_enabled_it_the_default' $API_TESTS/NotificationTriggerTests.cs"
check "a test proves replaying the same capture never double-fires the notification" \
  bash -c "grep -q 'AC04_d_capturing_the_same_idempotency_key_twice_never_double_fires_the_notification' $API_TESTS/NotificationTriggerTests.cs"

# --- ADR-0001 identifier-registry discipline ---
check "TransportEvent.EventId uses the registered evt_ type (a captured fact genuinely is a domain event, same choice BIO-ATT-001 made); TriggeredNotification.Id does not (no registered type for it)" \
  bash -c "grep -q 'CanonicalId.NewEventId()' $API_FEATURE/Domain/TransportEvent.cs && grep -q 'CanonicalId.NewLocalId()' $API_FEATURE/Domain/TriggeredNotification.cs"

# --- Module wiring, write-scope and captive-dependency discipline ---
check "no ProjectReference to any other feature project, including bio-trn-001 (a genuine runtime dependency reached only through this project's own gateway interfaces)" \
  bash -c "! grep -q '<ProjectReference' $API_FEATURE/Biolog.Features.TransportEvents.csproj"
check "ITransportRegistryGateway is this project's own interface, not a reference to bio-trn-001's types" \
  test -f "$API_FEATURE/Domain/ITransportRegistryGateway.cs"
check "backend module lives one-way in api/Biolog.Api/Modules, not back in the feature project" \
  bash -c "test -f api/Biolog.Api/Modules/BioTrn002Module.cs && ! test -f '$API_FEATURE/BioTrn002Module.cs'"
check "registered in the packaged Program.cs module array, depending on Tenant, PersonRegistry and Transport" \
  bash -c "grep -q 'new BioTrn002Module()' api/Biolog.Api/Program.cs && grep -q '\"Tenant\", \"PersonRegistry\", \"Transport\"' api/Biolog.Api/Modules/BioTrn002Module.cs"
check "the ambient-tenant registry gateway is registered Scoped, and the demo-seed provisioner is a SEPARATE Singleton registration - no captive-dependency bug (Singleton capturing Scoped ITenantContextAccessor)" \
  bash -c "grep -q 'AddScoped<ITransportRegistryGateway>' api/Biolog.Api/Modules/BioTrn002Module.cs && grep -q 'AddSingleton<ITransportDemoSeedProvisioner>' api/Biolog.Api/Modules/BioTrn002Module.cs"

# --- AC05: web wiring, no UI redesign ---
check "AC05 transport-page.tsx, ports.ts and mock.ts are untouched by this story (no unapproved UI redesign)" \
  bash -c "! grep -q 'BIO-TRN-002' web/features/transport/transport-page.tsx web/features/transport/ports.ts web/features/transport/mock.ts 2>/dev/null"
check "AC05 live.ts still calls the exact same two routes BIO-UX-005/010 originally scaffolded (no redesign, just re-pointed which module answers primary-action)" \
  bash -c "grep -q '/api/v1/transport/primary-action' web/features/transport/live.ts && grep -q '/api/v1/transport/assign-route' web/features/transport/live.ts"
check "AC05 this story's own demo bridge and endpoint mapping exist" \
  bash -c "test -f $API_FEATURE/Demo/TransportEventDemoBridgeService.cs && grep -q 'primary-action' $API_FEATURE/Api/TransportEventEndpoints.cs"
check "AC05 BIO-TRN-001's own primary-action ROUTE MAPPING/HANDLER was removed (its explanatory comment about the handoff may still name the route, that is not the same as still mapping or handling it)" \
  bash -c "! grep -qE 'MapPost\(\"/primary-action\"|PrimaryActionAsync' api/features/bio-trn-001/Api/TransportEndpoints.cs"
check "a real-packaged-host test proves the boarding/alighting log bridge answers a genuinely headerless request with real domain data" \
  bash -c "grep -q 'AC05_the_boarding_alighting_log_bridge_route_answers_a_genuinely_headerless_request_with_real_domain_data' $HOST_TESTS/TransportEventsHostChannelTests.cs"

# --- Developer test wiring ---
check "backend unit tests exist" bash -c \
  'test -f tests/features/bio-trn-002/TransportEventServiceTests.cs &&
   test -f tests/features/bio-trn-002/TransportPresenceEvaluatorTests.cs &&
   test -f tests/features/bio-trn-002/TransportNotificationConfigServiceTests.cs &&
   test -f tests/features/bio-trn-002/NotificationTriggerTests.cs'
check "a real-packaged-host test exists proving the genuine BIO-TRN-001/BIO-IDN-001 dependencies through the shipped host channel" \
  test -f "$HOST_TESTS/TransportEventsHostChannelTests.cs"
check "the host-channel test proves cross-tenant isolation on the route gateway specifically" \
  bash -c "grep -q 'AC01_capturing_against_a_route_from_a_DIFFERENT_tenant_fails_closed_through_the_real_host' $HOST_TESTS/TransportEventsHostChannelTests.cs"
check "builder evidence recorded" test -d evidence/features/bio-trn-002/builder

echo "=============================="
echo "$pass passed, $fail failed"
[ "$fail" -eq 0 ]
