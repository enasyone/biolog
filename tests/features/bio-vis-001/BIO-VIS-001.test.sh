#!/usr/bin/env bash
# Offline-safe structural checks for BIO-VIS-001 (visitor preregistration and host approval).
# Behavioural assertions live in the executable xunit suites under tests/features/bio-vis-001/
# (see docs/features/bio-vis-001/README.md).
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"
pass=0; fail=0
check() { local label="$1"; shift; if "$@"; then echo "PASS  $label"; pass=$((pass+1)); else echo "FAIL  $label"; fail=$((fail+1)); fi; }

FEATURE=api/features/bio-vis-001

# --- Project shape ---
check "feature project exists" test -f "$FEATURE/Biolog.Features.Visitors.csproj"
check "project targets the pinned .NET SDK's TFM" bash -c "grep -q '<TargetFramework>net10.0</TargetFramework>' '$FEATURE/Biolog.Features.Visitors.csproj'"
check "module lives one-way in api/Biolog.Api/Modules, not back in the feature project" \
  bash -c "test -f api/Biolog.Api/Modules/BioVis001Module.cs && ! test -f '$FEATURE/BioVis001Module.cs'"
check "registered in the packaged Program.cs module array" \
  bash -c "grep -q 'new BioVis001Module()' api/Biolog.Api/Program.cs"
check "Biolog.Api.csproj references the feature project" \
  bash -c "grep -q 'features/bio-vis-001/Biolog.Features.Visitors.csproj' api/Biolog.Api/Biolog.Api.csproj"
check "no ProjectReference from the feature project to any other feature project or Biolog.Api" \
  bash -c "! grep -q '<ProjectReference' '$FEATURE/Biolog.Features.Visitors.csproj'"

# --- AC01: visitor record links to PERSON/profile where appropriate, without forcing permanent identity ---
check "AC01 VisitorRecord's PersonId is nullable - a lead needs no permanent identity" \
  bash -c "grep -q 'string? PersonId' '$FEATURE/Domain/VisitorRecord.cs'"
check "AC01 FullName/HostPersonId are captured regardless of a person link" \
  bash -c "grep -q 'required string FullName' '$FEATURE/Domain/VisitorRecord.cs' && grep -q 'required string HostPersonId' '$FEATURE/Domain/VisitorRecord.cs'"
check "AC01 an optional person_id, when present, is existence-checked against BIO-IDN-001 (BIO-IDN-002-QA-01's lesson)" \
  bash -c "grep -q 'IPersonExistenceGateway' '$FEATURE/VisitorRecordService.cs' && grep -q 'PersonNotFound' '$FEATURE/VisitorRecordService.cs'"
check "AC01 no canonical vis_ identifier prefix is minted (disclosed, precedented boundary)" \
  bash -c "grep -q 'NewVisitorId' '$FEATURE/Identifiers/CanonicalId.cs' && ! grep -q '\"vis_' '$FEATURE/Identifiers/CanonicalId.cs'"
check "AC01 a test proves a lead needs no person link, and a repeat visitor can be linked" \
  bash -c "grep -q 'AC01_a_a_preregistered_visitor_carries_full_name_and_host_with_no_person_link_required' tests/features/bio-vis-001/VisitorRecordServiceTests.cs && grep -q 'AC01_b_a_repeat_visitor_can_be_linked_to_their_existing_person' tests/features/bio-vis-001/VisitorRecordServiceTests.cs"

# --- AC02: host approval states are auditable ---
check "AC02 HostApprovalStatus is Pending/Approved/Denied, decided exactly once" \
  bash -c "grep -q 'Pending,' '$FEATURE/Domain/VisitorEnums.cs' && grep -q 'Approved,' '$FEATURE/Domain/VisitorEnums.cs' && grep -q 'Denied,' '$FEATURE/Domain/VisitorEnums.cs'"
check "AC02 approve/deny are gated by permission AND a verified ADR-0006 provenance proof" \
  bash -c "grep -q 'DecidePermission' '$FEATURE/VisitorRecordService.cs' && grep -q 'provenanceGate.Require' '$FEATURE/Api/VisitorEndpoints.cs'"
check "AC02 every approve/deny/check-in/check-out transition is audited" \
  bash -c "grep -q 'visitor.approved' '$FEATURE/VisitorRecordService.cs' && grep -q 'visitor.denied' '$FEATURE/VisitorRecordService.cs' && grep -q 'visitor.checked_in' '$FEATURE/VisitorRecordService.cs' && grep -q 'visitor.checked_out' '$FEATURE/VisitorRecordService.cs'"
check "AC02 a test proves an already-decided request cannot be re-decided" \
  bash -c "grep -q 'AC02_e_an_already_decided_request_cannot_be_redecided' tests/features/bio-vis-001/VisitorRecordServiceTests.cs"
check "AC02 a test proves concurrent approve attempts on the same record only let one win (race, not just sequential)" \
  bash -c "grep -q 'AC02_f_concurrent_approve_attempts_on_the_same_pending_record_only_one_wins' tests/features/bio-vis-001/VisitorRecordServiceTests.cs"

# --- AC03: configurable fields include vehicle, NDA, safety briefing, watchlist and temporary credential options ---
check "AC03 VisitorRecord carries vehicle plate, NDA, safety briefing, watchlist result and temporary credential fields" \
  bash -c "grep -q 'VehiclePlate' '$FEATURE/Domain/VisitorRecord.cs' && grep -q 'NdaAccepted' '$FEATURE/Domain/VisitorRecord.cs' && grep -q 'SafetyBriefingCompleted' '$FEATURE/Domain/VisitorRecord.cs' && grep -q 'WatchlistScreeningResult' '$FEATURE/Domain/VisitorRecord.cs' && grep -q 'TemporaryCredentialRequested' '$FEATURE/Domain/VisitorRecord.cs' && grep -q 'TemporaryCredentialIssued' '$FEATURE/Domain/VisitorRecord.cs'"
check "AC03 VisitorTenantConfig makes each of the five fields independently configurable per tenant" \
  bash -c "grep -q 'RequireVehiclePlate' '$FEATURE/Domain/VisitorTenantConfig.cs' && grep -q 'RequireNda' '$FEATURE/Domain/VisitorTenantConfig.cs' && grep -q 'RequireSafetyBriefing' '$FEATURE/Domain/VisitorTenantConfig.cs' && grep -q 'RequireWatchlistScreening' '$FEATURE/Domain/VisitorTenantConfig.cs' && grep -q 'AllowTemporaryCredential' '$FEATURE/Domain/VisitorTenantConfig.cs'"
check "AC03 a tenant with no explicit config still has an effective default, never a null/missing config" \
  bash -c "grep -q 'static VisitorTenantConfig Default' '$FEATURE/Domain/VisitorTenantConfig.cs'"
check "AC03 config is CAS-versioned (compare-and-set), not blind-overwrite" \
  bash -c "grep -q 'TrySaveAsync' '$FEATURE/Domain/IVisitorConfigRepository.cs' && grep -q 'expectedVersion' '$FEATURE/Domain/IVisitorConfigRepository.cs'"
check "AC03 setting config is an authorization decision: requires a permission grant AND a verified ADR-0006 provenance proof" \
  bash -c "grep -q 'ManagePermission' '$FEATURE/VisitorConfigService.cs' && grep -q 'provenanceGate.Require' '$FEATURE/Api/VisitorEndpoints.cs'"
check "AC03 a test proves check-in enforces this tenant's required fields" \
  bash -c "grep -q 'AC03_a_check_in_enforces_this_tenants_required_fields' tests/features/bio-vis-001/VisitorRecordServiceTests.cs"

# --- AC04: automatic expiry is supported ---
check "AC04 ExpiresAt is captured on the record and never auto-mutated (no background job)" \
  bash -c "grep -q 'ExpiresAt' '$FEATURE/Domain/VisitorRecord.cs'"
check "AC04 effective status is a pure function (record, now) -> status, computed at read time, distinct enum type from the persisted status" \
  bash -c "grep -q 'static class VisitorStatusEvaluator' '$FEATURE/Domain/VisitorStatusEvaluator.cs' && grep -q 'enum EffectiveVisitStatus' '$FEATURE/Domain/VisitorEnums.cs' && ! grep -q 'Repository\|AuditSink' '$FEATURE/Domain/VisitorStatusEvaluator.cs'"
check "AC04 a test proves the identical stored record classifies differently at two different read times" \
  bash -c "grep -q 'the_identical_stored_record_classifies_differently_at_two_different_read_times_proving_expiry_is_computed_not_stored' tests/features/bio-vis-001/VisitorStatusEvaluatorTests.cs"
check "AC04 a test proves check-in is blocked once a preregistration has expired" \
  bash -c "grep -q 'AC04_b_check_in_is_blocked_once_a_preregistration_has_expired' tests/features/bio-vis-001/VisitorRecordServiceTests.cs"

# --- AC05: the approved web surface (/visitors) is connected through VisitorService to a live implementation ---
check "AC05 the three existing live adapters' endpoints are mapped for real (no route/verb change to web/features/visitors/live.ts)" \
  bash -c "grep -q '\"/primary-action\"' '$FEATURE/Api/VisitorEndpoints.cs' && grep -q '\"/check-out-visitor\"' '$FEATURE/Api/VisitorEndpoints.cs' && grep -q '\"/view-visit-history\"' '$FEATURE/Api/VisitorEndpoints.cs'"
check "AC05 web/features/visitors/live.ts still calls the exact same three routes BIO-UX-005/010 originally scaffolded (no redesign, just connected)" \
  bash -c "grep -q \"'/api/v1/visitors/primary-action'\" web/features/visitors/live.ts && grep -q \"'/api/v1/visitors/check-out-visitor'\" web/features/visitors/live.ts && grep -q \"'/api/v1/visitors/view-visit-history'\" web/features/visitors/live.ts"
check "AC05 visitors-page.tsx, ports.ts and mock.ts are untouched by this story (no unapproved UI redesign)" \
  bash -c "! grep -q 'BIO-VIS-001' web/features/visitors/visitors-page.tsx web/features/visitors/ports.ts web/features/visitors/mock.ts"
check "AC05 web/next.config.mjs proxies /api/v1/visitors/* to the real backend" \
  bash -c "grep -q \"'/api/v1/visitors/:path\\*'\" web/next.config.mjs"
check "AC05 the demo bridge exercises real domain preregistration/approval/check-in/check-out logic, not a canned string" \
  bash -c "grep -q 'VisitorRecord.Preregister' '$FEATURE/Demo/VisitorDemoBridgeService.cs' && grep -q '.CheckIn(' '$FEATURE/Demo/VisitorDemoBridgeService.cs' && grep -q '.CheckOut(' '$FEATURE/Demo/VisitorDemoBridgeService.cs'"

echo
echo "BIO-VIS-001 structural checks: $pass passed, $fail failed"
[ "$fail" -eq 0 ]
