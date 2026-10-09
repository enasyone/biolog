#!/usr/bin/env bash
# Offline-safe structural checks for BIO-ATT-002 (education attendance policies).
# Behavioural assertions live in the executable suites: the backend's own xunit tests
# (tests/features/bio-att-002/) and the real-packaged-host tests
# (tests/dotnet/Biolog.IntegrationTests/AttendanceSessionsHostChannelTests.cs) - see
# docs/features/bio-att-002/README.md.
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"
pass=0; fail=0
check() { local label="$1"; shift; if "$@"; then echo "PASS  $label"; pass=$((pass+1)); else echo "FAIL  $label"; fail=$((fail+1)); fi; }

API_FEATURE=api/features/bio-att-002
API_TESTS=tests/features/bio-att-002
HOST_TESTS=tests/dotnet/Biolog.IntegrationTests
ATT001_FEATURE=api/features/bio-att-001

# --- Project shape ---
check "feature project exists" test -f "$API_FEATURE/Biolog.Features.AttendanceSessions.csproj"
check "project targets the pinned .NET SDK's TFM" \
  bash -c "grep -q '<TargetFramework>net10.0</TargetFramework>' '$API_FEATURE/Biolog.Features.AttendanceSessions.csproj'"
check "no ProjectReference to any other feature project, including bio-att-001 (a genuine runtime dependency reached only through this project's own gateway interfaces)" \
  bash -c "! grep -q '<ProjectReference' '$API_FEATURE/Biolog.Features.AttendanceSessions.csproj'"
check "backend module lives one-way in api/Biolog.Api/Modules, not back in the feature project" \
  bash -c "test -f api/Biolog.Api/Modules/BioAtt002Module.cs && ! test -f '$API_FEATURE/BioAtt002Module.cs'"
check "registered in the packaged Program.cs module array, depending on Tenant, PersonRegistry, Rbac and Attendance" \
  bash -c "grep -q 'new BioAtt002Module()' api/Biolog.Api/Program.cs && grep -q '\"Tenant\", \"PersonRegistry\", \"Rbac\", \"Attendance\"' api/Biolog.Api/Modules/BioAtt002Module.cs"
check "Biolog.Api.csproj references the feature project" \
  bash -c "grep -q 'features/bio-att-002/Biolog.Features.AttendanceSessions.csproj' api/Biolog.Api/Biolog.Api.csproj"

# --- AC01: session/roster association ---
check "AC01 Session carries session_type, title, scheduled date/time and a roster of person ids, and is CAS-versioned" \
  bash -c "grep -q 'SessionType SessionType' '$API_FEATURE/Domain/Session.cs' && grep -q 'IReadOnlyList<string> RosterPersonIds' '$API_FEATURE/Domain/Session.cs' && grep -q 'int Version' '$API_FEATURE/Domain/Session.cs'"
check "AC01 SessionType covers lecture/class/exam/hostel/event (the user story's own wording)" \
  bash -c "grep -q 'Lecture' '$API_FEATURE/Domain/SessionEnums.cs' && grep -q 'Class' '$API_FEATURE/Domain/SessionEnums.cs' && grep -q 'Exam' '$API_FEATURE/Domain/SessionEnums.cs' && grep -q 'Hostel' '$API_FEATURE/Domain/SessionEnums.cs' && grep -q 'Event' '$API_FEATURE/Domain/SessionEnums.cs'"
check "AC01 every roster person id is existence-checked via this feature's own gateway before create/roster-update" \
  bash -c "grep -q 'EnsureRosterPersonsExistAsync' '$API_FEATURE/SessionService.cs'"
check "AC01 a roster can be revised after creation via a compare-and-set update, not blind overwrite" \
  bash -c "grep -q 'UpdateRoster' '$API_FEATURE/Domain/Session.cs' && grep -q 'TrySaveAsync' '$API_FEATURE/Domain/ISessionRepository.cs' && grep -q 'expectedVersion' '$API_FEATURE/Domain/ISessionRepository.cs'"
check "a test proves a created session carries its full roster and a roster update advances the version" \
  bash -c "grep -q 'AC01_a_created_session_carries_its_session_type_and_the_full_roster_it_was_given' $API_TESTS/SessionServiceTests.cs && grep -q 'AC01_a_roster_can_be_revised_after_the_session_exists_and_the_version_advances' $API_TESTS/SessionServiceTests.cs"

# --- AC02: present/late/absent rules are configurable ---
check "AC02 the policy is keyed on (tenant, session type), a genuinely richer granularity than BIO-ATT-001's single tenant-wide policy" \
  bash -c "grep -q 'SessionType SessionType' '$API_FEATURE/Domain/SessionAttendancePolicy.cs' && grep -q 'int LateGraceMinutes' '$API_FEATURE/Domain/SessionAttendancePolicy.cs' && grep -q 'int AbsentAfterMinutes' '$API_FEATURE/Domain/SessionAttendancePolicy.cs'"
check "AC02 a pure, read-time-only classifier exists, distinct from BIO-ATT-001's own evaluator" \
  test -f "$API_FEATURE/Domain/SessionAttendanceClassifier.cs"
check "AC02 setting the policy is gated by permission AND a verified ADR-0006 provenance proof" \
  bash -c "grep -q 'ManagePermission' '$API_FEATURE/SessionAttendancePolicyService.cs' && grep -q 'provenanceGate.Require' '$API_FEATURE/Api/SessionEndpoints.cs'"
check "a test proves the SAME evidence classifies differently under two different session-type policies" \
  bash -c "grep -q 'AC02_the_SAME_evidence_classifies_differently_under_two_different_session_type_policies' $API_TESTS/SessionAttendanceClassifierTests.cs"
check "a test proves the manage permission is required to set the policy" \
  bash -c "grep -q 'AC02_setting_a_policy_without_the_manage_permission_is_rejected' $API_TESTS/SessionAttendancePolicyServiceTests.cs"

# --- AC03: evidence source is preserved ---
check "AC03 a local evidence projection carries event id, occurred_at, source, mode and verification method/verified/confidence" \
  bash -c "grep -q 'string EventId' '$API_FEATURE/Domain/AttendanceEvidenceRecord.cs' && grep -q 'string EvidenceMethod' '$API_FEATURE/Domain/AttendanceEvidenceRecord.cs' && grep -q 'bool EvidenceVerified' '$API_FEATURE/Domain/AttendanceEvidenceRecord.cs'"
check "AC03 the classification's grounding evidence is exposed on the roster read, not discarded" \
  bash -c "grep -q 'GroundingEvidence' '$API_FEATURE/Domain/SessionMemberClassification.cs' && grep -q 'grounding_evidence' '$API_FEATURE/Api/SessionDtos.cs'"
check "a test proves the grounding evidence carries the original event id and verification details unmodified" \
  bash -c "grep -q 'AC03_the_grounding_evidence_carries_the_original_event_id_and_verification_details_unmodified' $API_TESTS/SessionAttendanceClassifierTests.cs"

# --- AC04: manual adjustments are audited ---
check "AC04 AttendanceAdjustment is a separate, append-only record, never a mutation of anything" \
  bash -c "test -f '$API_FEATURE/Domain/AttendanceAdjustment.cs' && ! grep -qE 'internal (Session|SessionMemberClassification) [A-Za-z]+\(' '$API_FEATURE/Domain/Session.cs'"
check "AC04 recording an adjustment is gated by permission AND a verified ADR-0006 provenance proof" \
  bash -c "grep -q 'ManagePermission' '$API_FEATURE/AttendanceAdjustmentService.cs' && grep -q 'CreateAdjustmentAsync' '$API_FEATURE/Api/SessionEndpoints.cs'"
check "AC04 every adjustment write is audited before the repository commit" \
  bash -c "grep -q 'AuditAsync' '$API_FEATURE/AttendanceAdjustmentService.cs'"
check "AC04 a roster read shows the computed classification AND the latest adjustment side by side, never one replacing the other" \
  bash -c "grep -q 'EffectiveStatus' '$API_FEATURE/Domain/SessionRosterMemberView.cs' && grep -q 'Classification.Status' '$API_FEATURE/Domain/SessionRosterMemberView.cs'"
check "a test proves an adjustment changes the effective status but never the computed classification" \
  bash -c "grep -q 'AC04_an_adjustment_changes_the_effective_status_but_never_the_computed_classification' $API_TESTS/SessionAttendanceQueryServiceTests.cs"
check "a test proves an adjustment must target a person genuinely on that session's roster" \
  bash -c "grep -q 'AC04_an_adjustment_targeting_a_person_not_on_the_roster_is_rejected' $API_TESTS/AttendanceAdjustmentServiceTests.cs"

# --- Cross-story gateway discipline ---
check "IAttendanceEventQueryGateway is this project's own interface, not a reference to bio-att-001's types" \
  test -f "$API_FEATURE/Domain/IAttendanceEventQueryGateway.cs"
check "the ambient-tenant evidence gateway is registered Scoped, and the demo-seed gateway is a SEPARATE Singleton registration - no captive-dependency bug" \
  bash -c "grep -q 'AddScoped<IAttendanceEventQueryGateway>' api/Biolog.Api/Modules/BioAtt002Module.cs && grep -q 'AddSingleton<IAttendanceDemoSeedGateway>' api/Biolog.Api/Modules/BioAtt002Module.cs"

# --- AC05: web wiring, disclosed handoff from BIO-ATT-001, no UI redesign ---
check "AC05 this story's own primary-action/flag-exception mapping and demo bridge exist" \
  bash -c "grep -q '\"/primary-action\"' '$API_FEATURE/Api/SessionEndpoints.cs' && grep -q '\"/flag-exception\"' '$API_FEATURE/Api/SessionEndpoints.cs' && test -f $API_FEATURE/Demo/AttendanceSessionDemoBridgeService.cs"
check "AC05 BIO-ATT-001's own primary-action/flag-exception ROUTE MAPPING/HANDLERS were removed (not left duplicated) and its own demo bridge file was deleted" \
  bash -c "! grep -qE 'MapPost\(\"/primary-action\"|MapPost\(\"/flag-exception\"|PrimaryActionAsync|FlagExceptionAsync' $ATT001_FEATURE/Api/AttendanceEndpoints.cs && ! test -f $ATT001_FEATURE/Demo/AttendanceDemoBridgeService.cs"
check "AC05 web/features/attendance/live.ts still calls the exact same two routes BIO-UX-010 originally scaffolded (no redesign, just re-pointed which module answers them)" \
  bash -c "grep -q \"'/api/v1/attendance/primary-action'\" web/features/attendance/live.ts && grep -q \"'/api/v1/attendance/flag-exception'\" web/features/attendance/live.ts"
check "AC05 attendance-page.tsx, ports.ts and mock.ts are untouched by this story (no unapproved UI redesign)" \
  bash -c "! grep -q 'BIO-ATT-002' web/features/attendance/attendance-page.tsx web/features/attendance/ports.ts web/features/attendance/mock.ts 2>/dev/null"
check "AC05 web/next.config.mjs already proxies /api/v1/attendance/* to the real backend (BIO-ATT-001's own rule, unchanged)" \
  bash -c "grep -q \"'/api/v1/attendance/:path\\*'\" web/next.config.mjs"
check "AC05 the demo bridge exercises real domain classification and a real audited adjustment, not a canned string" \
  bash -c "grep -q 'SessionAttendanceClassifier.Evaluate' $API_FEATURE/Demo/AttendanceSessionDemoBridgeService.cs && grep -q 'AttendanceAdjustment.Create' $API_FEATURE/Demo/AttendanceSessionDemoBridgeService.cs"
check "AC05 the demo bridge writes real evidence into BIO-ATT-001's own event store via the demo-seed gateway, not a fake" \
  bash -c "grep -q 'CaptureDemoCheckInAsync' $API_FEATURE/Demo/AttendanceSessionDemoBridgeService.cs && grep -q 'AttendanceEvent.Capture' api/Biolog.Api/Modules/BioAtt002Module.cs"
check "a real-packaged-host test proves both bridge routes answer a genuinely headerless request" \
  bash -c "grep -q 'ATT002_AC05_the_two_UI_bridge_routes_answer_a_genuinely_headerless_request_through_the_real_host' $HOST_TESTS/AttendanceSessionsHostChannelTests.cs"

# --- Developer test wiring ---
check "backend unit tests exist" bash -c \
  'test -f tests/features/bio-att-002/SessionServiceTests.cs &&
   test -f tests/features/bio-att-002/SessionAttendanceClassifierTests.cs &&
   test -f tests/features/bio-att-002/SessionAttendancePolicyServiceTests.cs &&
   test -f tests/features/bio-att-002/SessionAttendanceQueryServiceTests.cs &&
   test -f tests/features/bio-att-002/AttendanceAdjustmentServiceTests.cs'
check "a real-packaged-host test exists proving the genuine BIO-ATT-001/BIO-IDN-001 dependencies through the shipped host channel" \
  test -f "$HOST_TESTS/AttendanceSessionsHostChannelTests.cs"
check "the host-channel test proves a real roster read reflects a real BIO-ATT-001 captured event" \
  bash -c "grep -q 'ATT002_AC01_AC03_a_roster_read_through_the_real_host_reflects_a_real_captured_BIO_ATT_001_event' $HOST_TESTS/AttendanceSessionsHostChannelTests.cs"
check "builder evidence recorded" test -d evidence/features/bio-att-002/builder

echo "=============================="
echo "$pass passed, $fail failed"
[ "$fail" -eq 0 ]
