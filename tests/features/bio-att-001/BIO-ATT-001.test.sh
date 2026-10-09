#!/usr/bin/env bash
# Offline-safe structural checks for BIO-ATT-001 (attendance event core). Behavioural assertions
# live in the executable xunit suites under tests/features/bio-att-001/ (see
# docs/features/bio-att-001/README.md).
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"
pass=0; fail=0
check() { local label="$1"; shift; if "$@"; then echo "PASS  $label"; pass=$((pass+1)); else echo "FAIL  $label"; fail=$((fail+1)); fi; }

FEATURE=api/features/bio-att-001

# --- Project shape ---
check "feature project exists" test -f "$FEATURE/Biolog.Features.Attendance.csproj"
check "project targets the pinned .NET SDK's TFM" bash -c "grep -q '<TargetFramework>net10.0</TargetFramework>' '$FEATURE/Biolog.Features.Attendance.csproj'"
check "module lives one-way in api/Biolog.Api/Modules, not back in the feature project" \
  bash -c "test -f api/Biolog.Api/Modules/BioAtt001Module.cs && ! test -f '$FEATURE/BioAtt001Module.cs'"
check "registered in the packaged Program.cs module array" \
  bash -c "grep -q 'new BioAtt001Module()' api/Biolog.Api/Program.cs"
check "Biolog.Api.csproj references the feature project" \
  bash -c "grep -q 'features/bio-att-001/Biolog.Features.Attendance.csproj' api/Biolog.Api/Biolog.Api.csproj"
check "no ProjectReference from the feature project to any other feature project or Biolog.Api" \
  bash -c "! grep -q '<ProjectReference' '$FEATURE/Biolog.Features.Attendance.csproj'"

# --- AC01: event captures person, source/device, location, time, mode and confidence/verification evidence ---
check "AC01 AttendanceEvent carries person, source, device, location, mode, occurred_at and evidence" \
  bash -c "grep -q 'PersonId' '$FEATURE/Domain/AttendanceEvent.cs' && grep -q 'Source' '$FEATURE/Domain/AttendanceEvent.cs' && grep -q 'DeviceId' '$FEATURE/Domain/AttendanceEvent.cs' && grep -q 'Location' '$FEATURE/Domain/AttendanceEvent.cs' && grep -q 'Mode' '$FEATURE/Domain/AttendanceEvent.cs' && grep -q 'OccurredAt' '$FEATURE/Domain/AttendanceEvent.cs'"
check "AC01 VerificationEvidence carries method, verified and confidence" \
  bash -c "grep -q 'VerificationMethod Method' '$FEATURE/Domain/VerificationEvidence.cs' && grep -q 'bool Verified' '$FEATURE/Domain/VerificationEvidence.cs' && grep -q 'double? Confidence' '$FEATURE/Domain/VerificationEvidence.cs'"
check "AC01 device_id is required when source is device, forbidden otherwise" \
  bash -c "grep -q 'device_id is required when source' '$FEATURE/Domain/AttendanceEvent.cs' && grep -q 'device_id may only be present when source' '$FEATURE/Domain/AttendanceEvent.cs'"
check "AC01 a person_id is existence-checked against BIO-IDN-001 before capture (BIO-IDN-002-QA-01's lesson, applied from the start)" \
  bash -c "grep -q 'IPersonExistenceGateway' '$FEATURE/AttendanceEventService.cs' && grep -q 'PersonNotFound' '$FEATURE/AttendanceEventService.cs'"
check "AC01 a test proves every field lands on the captured event" \
  bash -c "grep -q 'AC01_a_a_captured_event_carries_person_source_device_location_time_mode_and_evidence' tests/features/bio-att-001/AttendanceEventServiceTests.cs"

# --- AC02: raw event is distinct from derived attendance status ---
check "AC02 AttendanceEvent has no status/derived-classification field of any kind" \
  bash -c "! grep -qi 'AttendanceStatusValue\|DerivedAttendanceStatus\|public.*Status' '$FEATURE/Domain/AttendanceEvent.cs'"
check "AC02 DerivedAttendanceStatus is a separate, non-persisted computed record" \
  bash -c "grep -q 'record DerivedAttendanceStatus' '$FEATURE/Domain/DerivedAttendanceStatus.cs'"
check "AC02 status evaluation is a pure function (events, policy) -> status, no repository/audit dependency" \
  bash -c "grep -q 'static class AttendanceStatusEvaluator' '$FEATURE/Domain/AttendanceStatusEvaluator.cs' && ! grep -q 'Repository\|AuditSink' '$FEATURE/Domain/AttendanceStatusEvaluator.cs'"
check "AC02 a test proves the identical raw event set classifies differently under two different policies" \
  bash -c "grep -q 'AC02_and_AC04_together_the_identical_raw_event_set_classifies_differently_under_two_different_policies' tests/features/bio-att-001/AttendanceStatusEvaluatorTests.cs"
check "AC02 a test proves setting a policy after capture never touches the raw event" \
  bash -c "grep -q 'setting_a_stricter_policy_after_capture_changes_the_derived_status_without_touching_the_raw_event' tests/features/bio-att-001/AttendanceStatusServiceTests.cs"

# --- AC03: duplicate/idempotent capture is handled ---
check "AC03 AttendanceEvent carries a producer-minted idempotency key and a content fingerprint" \
  bash -c "grep -q 'IdempotencyKey' '$FEATURE/Domain/AttendanceEvent.cs' && grep -q 'ContentFingerprint' '$FEATURE/Domain/AttendanceEvent.cs'"
check "AC03 a redelivery with identical content is answered idempotently, a redelivery with different content is a conflict" \
  bash -c "grep -q 'ContentFingerprint == fingerprint' '$FEATURE/AttendanceEventService.cs' && grep -q 'IdempotencyConflict' '$FEATURE/AttendanceEventService.cs'"
check "AC03 capture is serialized per (tenant, idempotency_key) via a dedicated lock registry" \
  bash -c "grep -q 'AttendanceEventLockRegistry' '$FEATURE/AttendanceLockRegistries.cs' && grep -q 'AttendanceEventLockRegistry' '$FEATURE/AttendanceEventService.cs'"
check "AC03 a test proves identical redelivery returns the same event without duplicating storage" \
  bash -c "grep -q 'AC03_a_identical_redelivery_of_the_same_idempotency_key_returns_the_same_event_and_does_not_duplicate' tests/features/bio-att-001/AttendanceEventServiceTests.cs"
check "AC03 a test proves reused key with different content is rejected, not silently overwritten" \
  bash -c "grep -q 'AC03_b_reusing_an_idempotency_key_with_different_content_is_a_conflict_not_a_silent_overwrite' tests/features/bio-att-001/AttendanceEventServiceTests.cs"
check "AC03 a test proves concurrent captures with the same key serialize to exactly one event (race, not just a sequential re-check)" \
  bash -c "grep -q 'AC03_c_concurrent_captures_with_the_same_idempotency_key_serialize_and_only_one_event_is_ever_created' tests/features/bio-att-001/AttendanceEventServiceTests.cs"

# --- AC04: tenant policies define lateness/absence/attendance interpretation ---
check "AC04 AttendancePolicy carries an interpretation mode, expected start time and late grace minutes" \
  bash -c "grep -q 'InterpretationMode' '$FEATURE/Domain/AttendancePolicy.cs' && grep -q 'ExpectedStartTime' '$FEATURE/Domain/AttendancePolicy.cs' && grep -q 'LateGraceMinutes' '$FEATURE/Domain/AttendancePolicy.cs'"
check "AC04 interpretation mode covers education, workforce and presence (MASTER-TECHNICAL-SPECIFICATION.md s10)" \
  bash -c "grep -q 'Education,' '$FEATURE/Domain/AttendanceEnums.cs' && grep -q 'Workforce,' '$FEATURE/Domain/AttendanceEnums.cs' && grep -q 'Presence,' '$FEATURE/Domain/AttendanceEnums.cs'"
check "AC04 a tenant with no explicit policy still has an effective default, never a null/missing policy" \
  bash -c "grep -q 'static AttendancePolicy Default' '$FEATURE/Domain/AttendancePolicy.cs'"
check "AC04 setting a policy is an authorization decision: requires a permission grant AND a verified ADR-0006 provenance proof" \
  bash -c "grep -q 'ManagePermission' '$FEATURE/AttendancePolicyService.cs' && grep -q 'provenanceGate.Require' '$FEATURE/Api/AttendanceEndpoints.cs'"
check "AC04 policy is CAS-versioned (compare-and-set), not blind-overwrite" \
  bash -c "grep -q 'TrySaveAsync' '$FEATURE/Domain/IAttendancePolicyRepository.cs' && grep -q 'expectedVersion' '$FEATURE/Domain/IAttendancePolicyRepository.cs'"
check "AC04 a test proves the manage permission is required" \
  bash -c "grep -q 'AC04_b_setting_a_policy_requires_the_manage_permission' tests/features/bio-att-001/AttendancePolicyServiceTests.cs"

# --- AC05: primary-action/flag-exception moved to BIO-ATT-002 (Tunde, same owner) - see
# Api/AttendanceEndpoints.cs's own header and docs/features/bio-att-002/README.md "Why this story
# edits BIO-ATT-001's own endpoint mapping". This story's own /events, /status and /policy routes
# are this feature's real, permanent surface and are unaffected. ---
check "AC05 primary-action/flag-exception are no longer mapped or handled here; events/status/policy still are" \
  bash -c "! grep -q '\"/primary-action\"' '$FEATURE/Api/AttendanceEndpoints.cs' && ! grep -q '\"/flag-exception\"' '$FEATURE/Api/AttendanceEndpoints.cs' && grep -q '\"/events\"' '$FEATURE/Api/AttendanceEndpoints.cs' && grep -q '\"/policy\"' '$FEATURE/Api/AttendanceEndpoints.cs'"
check "AC05 the now-superseded demo bridge file was removed, not left dangling" \
  bash -c "! test -f '$FEATURE/Demo/AttendanceDemoBridgeService.cs'"
check "AC05 web/features/attendance/live.ts still calls the exact same two routes BIO-UX-010 originally scaffolded (BIO-ATT-002 now answers them, no redesign)" \
  bash -c "grep -q \"'/api/v1/attendance/primary-action'\" web/features/attendance/live.ts && grep -q \"'/api/v1/attendance/flag-exception'\" web/features/attendance/live.ts"
check "AC05 attendance-page.tsx, ports.ts and mock.ts are untouched by this story (no unapproved UI redesign)" \
  bash -c "! grep -q 'BIO-ATT-001' web/features/attendance/attendance-page.tsx web/features/attendance/ports.ts web/features/attendance/mock.ts"
check "AC05 web/next.config.mjs proxies /api/v1/attendance/* to the real backend" \
  bash -c "grep -q \"'/api/v1/attendance/:path\\*'\" web/next.config.mjs"

echo
echo "BIO-ATT-001 structural checks: $pass passed, $fail failed"
[ "$fail" -eq 0 ]
