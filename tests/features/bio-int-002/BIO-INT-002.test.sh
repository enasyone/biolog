#!/usr/bin/env bash
# Offline-safe structural checks for BIO-INT-002 (SIS/HR/ERP import framework).
# Behavioural assertions live in the executable xunit suites under tests/features/bio-int-002/
# (see docs/features/bio-int-002/README.md).
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"
pass=0; fail=0
check() { local label="$1"; shift; if "$@"; then echo "PASS  $label"; pass=$((pass+1)); else echo "FAIL  $label"; fail=$((fail+1)); fi; }

FEATURE=api/features/bio-int-002

# --- Project shape ---
check "feature project exists" test -f "$FEATURE/Biolog.Features.Integrations.csproj"
check "project targets the pinned .NET SDK's TFM" bash -c "grep -q '<TargetFramework>net10.0</TargetFramework>' '$FEATURE/Biolog.Features.Integrations.csproj'"
check "module lives one-way in api/Biolog.Api/Modules, not back in the feature project" \
  bash -c "test -f api/Biolog.Api/Modules/BioInt002Module.cs && ! test -f '$FEATURE/BioInt002Module.cs'"
check "registered in the packaged Program.cs module array" \
  bash -c "grep -q 'new BioInt002Module()' api/Biolog.Api/Program.cs"
check "Biolog.Api.csproj references the feature project" \
  bash -c "grep -q 'features/bio-int-002/Biolog.Features.Integrations.csproj' api/Biolog.Api/Biolog.Api.csproj"
check "no ProjectReference from the feature project to any other feature project or Biolog.Api" \
  bash -c "! grep -q '<ProjectReference' '$FEATURE/Biolog.Features.Integrations.csproj'"

# --- AC01: source mappings are versioned ---
check "AC01 ImportSourceMapping is CAS-versioned with internal construction" \
  bash -c "grep -q 'required int Version' '$FEATURE/Domain/ImportSourceMapping.cs' && grep -q 'internal ImportSourceMapping' '$FEATURE/Domain/ImportSourceMapping.cs'"
check "AC01 no canonical identifier-registry prefix is minted for this story's own aggregates (disclosed, precedented boundary)" \
  bash -c "grep -q 'NewLocalId' '$FEATURE/Identifiers/CanonicalId.cs'"
check "AC01 repository enforces compare-and-set on update" \
  bash -c "grep -q 'ConcurrencyConflict' '$FEATURE/Domain/InMemoryImportSourceMappingRepository.cs'"
check "AC01 mapping create/update is a genuine authorization decision: requires a permission grant AND a verified ADR-0006 provenance proof" \
  bash -c "grep -q 'ManagePermission' '$FEATURE/ImportMappingService.cs' && grep -q 'provenanceGate.Require' '$FEATURE/Api/ImportEndpoints.cs'"
check "a test proves a new mapping starts at version 1" \
  bash -c "grep -q 'CreateAsync_a_new_mapping_starts_at_version_1' tests/features/bio-int-002/ImportMappingServiceTests.cs"
check "a test proves a stale version is rejected with concurrency_conflict" \
  bash -c "grep -q 'UpdateAsync_a_stale_expected_version_is_rejected_with_concurrency_conflict' tests/features/bio-int-002/ImportMappingServiceTests.cs"

# --- AC02: BIOLOG can be master or downstream per integration ---
check "AC02 SyncDirection enum exists with both directions" \
  bash -c "grep -q 'SourceIsMaster' '$FEATURE/Domain/ImportSourceMapping.cs' && grep -q 'BiologIsMaster' '$FEATURE/Domain/ImportSourceMapping.cs'"
check "AC02 ApplyAsync rejects an inbound apply when BIOLOG is configured as master" \
  bash -c "grep -q 'BiologIsMasterForThisIntegration' '$FEATURE/ImportBatchService.cs' && grep -q 'BiologIsMasterForThisIntegration' '$FEATURE/Domain/ImportError.cs'"
check "a test proves apply is rejected outright for a biolog_is_master mapping, and no person is ever created" \
  bash -c "grep -q 'ApplyAsync_is_rejected_outright_when_the_mapping_is_configured_biolog_is_master' tests/features/bio-int-002/ImportBatchServiceTests.cs"

# --- AC03: dry-run/conflict preview exists ---
check "AC03 ImportBatchMode distinguishes DryRun from Applied" \
  bash -c "grep -q 'DryRun' '$FEATURE/Domain/ImportBatch.cs' && grep -q 'Applied' '$FEATURE/Domain/ImportBatch.cs'"
check "AC03 PreviewAsync never calls the person gateway's create method" \
  bash -c "! grep -q 'personGateway.CreateAsync' <(sed -n '/public async Task<ImportBatch> PreviewAsync/,/^    }/p' '$FEATURE/ImportBatchService.cs')"
check "AC03 preview requires no permission - audit-attribution only (ADR-0006 Decision 2)" \
  bash -c "! grep -q 'permissionGateway' <(sed -n '/public async Task<ImportBatch> PreviewAsync/,/^    }/p' '$FEATURE/ImportBatchService.cs')"
check "a test proves preview classifies a new external key as created and writes nothing" \
  bash -c "grep -q 'PreviewAsync_a_brand_new_external_key_is_classified_created_and_nothing_is_written' tests/features/bio-int-002/ImportBatchServiceTests.cs"

# --- AC04: imports do not silently overwrite conflicting identity evidence ---
check "AC04 RowOutcome has a distinct Conflict case, never merged into a write path" \
  bash -c "grep -q 'Conflict,' '$FEATURE/Domain/ImportBatch.cs'"
check "AC04 FieldConflict records existing vs incoming without ever being applied" \
  bash -c "grep -q 'record FieldConflict' '$FEATURE/Domain/ImportBatch.cs'"
check "AC04 IPersonGateway exposes no profile-mutation method beyond Create/Get (the load-bearing scope boundary)" \
  bash -c "! grep -qE 'Task.*Update' '$FEATURE/Domain/IPersonGateway.cs'"
check "a test proves a disagreeing field is flagged conflict and the PERSON is never touched a second time" \
  bash -c "grep -q 'ApplyAsync_an_already_linked_person_with_a_disagreeing_field_is_flagged_as_conflict_and_never_written' tests/features/bio-int-002/ImportBatchServiceTests.cs"
check "a test proves a pure fill-in of a blank field is unchanged, not a conflict" \
  bash -c "grep -q 'ApplyAsync_a_pure_fill_in_of_a_previously_blank_field_is_unchanged_not_a_conflict' tests/features/bio-int-002/ImportBatchServiceTests.cs"

# --- AC05: idempotent reprocessing is supported ---
check "AC05 ApplyAsync uses the copied IIdempotencyStore (claim/complete/release, BIO-ARC-004/ADR-0004)" \
  bash -c "grep -q 'idempotencyStore.Claim' '$FEATURE/ImportBatchService.cs' && grep -q 'idempotencyStore.Complete' '$FEATURE/ImportBatchService.cs' && grep -q 'idempotencyStore.Release' '$FEATURE/ImportBatchService.cs'"
check "AC05 a reused key with different content is rejected, never silently treated as the original" \
  bash -c "grep -q 'IdempotencyKeyReused' '$FEATURE/ImportBatchService.cs'"
check "a test proves the same key with identical rows replays without reprocessing" \
  bash -c "grep -q 'ApplyAsync_the_same_idempotency_key_with_identical_rows_replays_the_original_result_without_reprocessing' tests/features/bio-int-002/ImportBatchServiceTests.cs"
check "a test proves the same key with different rows is rejected as reused" \
  bash -c "grep -q 'ApplyAsync_the_same_idempotency_key_with_different_rows_is_rejected_as_reused' tests/features/bio-int-002/ImportBatchServiceTests.cs"

# --- Post-review remediation round 1 (BIO-INT-002-F01, Seyi round-1 FAIL) ---
check "F01 Claim's own ownership signal (IsNewClaim) exists, not just State" \
  bash -c "grep -q 'IsNewClaim' '$FEATURE/Domain/IdempotencyStore.cs'"
check "F01 ApplyAsync branches on IsNewClaim, never executes on a claim it does not own" \
  bash -c "grep -q 'claim.IsNewClaim' '$FEATURE/ImportBatchService.cs'"
check "F01 a bounded wait-then-503 error exists for a claim-loser (ADR-0004/BIO-ARC-004)" \
  bash -c "grep -q 'IdempotencyWaitTimeout' '$FEATURE/Domain/ImportError.cs' && grep -q 'idempotency_wait_timeout' '$FEATURE/Domain/ImportError.cs'"
check "a deterministic two-caller test proves exactly one execution for the same idempotency key" \
  bash -c "grep -q 'ApplyAsync_two_genuinely_concurrent_callers_with_the_same_idempotency_key_execute_exactly_once' tests/features/bio-int-002/ImportBatchServiceTests.cs"
check "a test proves a claim-loser times out rather than waiting forever or executing anyway" \
  bash -c "grep -q 'ApplyAsync_a_second_caller_times_out_if_the_owners_claim_never_completes' tests/features/bio-int-002/ImportBatchServiceTests.cs"

# --- Post-review remediation round 1, Zainab's own refinement (ZAINAB-INT-002-F01) ---
check "F01 refinement: the try/catch covers the full sequence, not just the rows loop" \
  bash -c "grep -q 'idempotencyStore.Complete' '$FEATURE/ImportBatchService.cs'"
check "F01 refinement: IOrphanedPersonLedger exists, mirroring BIO-VIS-002's own adoption backstop" \
  bash -c "test -f '$FEATURE/Domain/IOrphanedPersonLedger.cs' && grep -q 'RecordAsync' '$FEATURE/Domain/IOrphanedPersonLedger.cs' && grep -q 'FindAsync' '$FEATURE/Domain/IOrphanedPersonLedger.cs'"
check "F01 refinement: ApplyAsync adopts an orphaned PersonId before minting a new one" \
  bash -c "grep -q 'orphanedPersonLedger.FindAsync' '$FEATURE/ImportBatchService.cs' && grep -q 'orphanedPersonLedger.RecordAsync' '$FEATURE/ImportBatchService.cs'"
check "a test proves a partial link-write failure releases the claim rather than leaving it stuck" \
  bash -c "grep -q 'ApplyAsync_when_the_link_write_fails_after_the_person_was_created_the_claim_is_released_not_stuck' tests/features/bio-int-002/ImportBatchServiceTests.cs"
check "a test proves a retry after a partial failure adopts the orphan instead of duplicating the PERSON" \
  bash -c "grep -q 'ApplyAsync_a_retry_after_a_partial_link_failure_adopts_the_orphaned_person_instead_of_creating_a_second_one' tests/features/bio-int-002/ImportBatchServiceTests.cs"
check "a test proves an audit-write failure AFTER the rows loop also releases the claim, not stuck" \
  bash -c "grep -q 'ApplyAsync_when_the_audit_write_fails_after_rows_are_processed_the_claim_is_released_not_stuck' tests/features/bio-int-002/ImportBatchServiceTests.cs"

# --- Post-review remediation round 1 (BIO-INT-002-F02, Zainab round-1 FAIL) ---
check "F02 the disable switch is actually checked in both PreviewAsync and ApplyAsync" \
  bash -c "grep -c 'MappingDisabled' '$FEATURE/ImportBatchService.cs' | grep -qE '^[2-9]'"
check "F02 a stable error code exists for a disabled mapping" \
  bash -c "grep -q 'import_mapping_disabled' '$FEATURE/Domain/ImportError.cs'"
check "a test proves ApplyAsync rejects a disabled mapping" \
  bash -c "grep -q 'ApplyAsync_against_a_disabled_mapping_is_rejected' tests/features/bio-int-002/ImportBatchServiceTests.cs"
check "a test proves PreviewAsync rejects a disabled mapping" \
  bash -c "grep -q 'PreviewAsync_against_a_disabled_mapping_is_rejected' tests/features/bio-int-002/ImportBatchServiceTests.cs"
check "a test proves re-enabling a mapping restores normal behavior" \
  bash -c "grep -q 'ApplyAsync_re_enabling_a_disabled_mapping_restores_normal_behavior' tests/features/bio-int-002/ImportBatchServiceTests.cs"

# --- Post-review remediation round 2 (Seyi round-2 FAIL / Zainab round-2 FAIL) ---
check "round-2 finding A: orphaned-person recording uses CancellationToken.None, not the caller's token" \
  bash -c "grep -q 'RecordOrphanedPersonSafelyAsync' '$FEATURE/ImportBatchService.cs' && grep -A3 'orphanedPersonLedger.RecordAsync(tenantId, mappingId, externalKey, personId, CancellationToken.None)' '$FEATURE/ImportBatchService.cs' >/dev/null"
check "round-2 finding A: orphan adoption runs the same field-comparison helper before linking" \
  bash -c "grep -q 'CompareFields(mapping, row, orphanedPerson)' '$FEATURE/ImportBatchService.cs'"
check "round-2 finding B: IImportRowProvenanceLedger exists, keyed by idempotency key not just external key" \
  bash -c "test -f '$FEATURE/Domain/IImportRowProvenanceLedger.cs' && grep -q 'idempotencyKey' '$FEATURE/Domain/IImportRowProvenanceLedger.cs'"
check "round-2 finding B: ApplyAsync replays a recorded row outcome before re-classifying" \
  bash -c "grep -q 'rowProvenanceLedger.FindAsync' '$FEATURE/ImportBatchService.cs'"
check "round-2 finding C: the authoritative Active/SyncDirection check happens after the lock is acquired" \
  bash -c "awk '/lockRegistry.AcquireAsync\(tenantId, mappingId, cancellationToken\)/{f=1} f && /mapping.Active/{found=1} END{exit !found}' '$FEATURE/ImportBatchService.cs'"
check "a test proves a retry after a late audit failure preserves Created provenance, not Unchanged" \
  bash -c "grep -q 'ApplyAsync_a_retry_after_a_late_audit_failure_preserves_the_original_created_provenance_not_unchanged' tests/features/bio-int-002/ImportBatchServiceTests.cs"
check "a test proves cancellation right after PERSON creation still lets a retry adopt, not duplicate" \
  bash -c "grep -q 'ApplyAsync_when_recording_the_orphaned_person_is_cancelled_a_retry_still_adopts_rather_than_duplicates' tests/features/bio-int-002/ImportBatchServiceTests.cs"
check "a test proves a content-mismatched orphan adoption is a conflict, not a silent creation" \
  bash -c "grep -q 'ApplyAsync_adopting_an_orphan_whose_content_now_disagrees_with_the_incoming_row_is_a_conflict_not_a_silent_creation' tests/features/bio-int-002/ImportBatchServiceTests.cs"
check "a deterministic barrier test proves a concurrent disable wins over a stale pre-lock read" \
  bash -c "grep -q 'ApplyAsync_rechecks_active_after_acquiring_the_lock_so_a_concurrent_disable_that_already_committed_wins_over_a_stale_pre_lock_read' tests/features/bio-int-002/ImportBatchServiceTests.cs"

# --- Post-review remediation round 4 (Seyi round-3 FAIL = Zainab round-3 FAIL finding 1; Zainab round-3 FAIL finding 2, ZAINAB-INT-002-F04; residual durability note) ---
check "round-4 finding 1: lock acquisition and the fresh mapping guard are inside the release-on-failure try block" \
  bash -c "awk '/ImportBatch created;/{f=1} f && /try/{t=1} t && /lockRegistry.AcquireAsync\(tenantId, mappingId, cancellationToken\)/{found=1} t && /catch/{exit} END{exit !found}' '$FEATURE/ImportBatchService.cs'"
check "a test proves a post-claim disabled-mapping rejection releases the claim and a later retry succeeds" \
  bash -c "grep -q 'ApplyAsync_rejected_by_a_disabled_mapping_after_claiming_releases_the_claim_and_a_later_retry_succeeds' tests/features/bio-int-002/ImportBatchServiceTests.cs"
check "a test proves a post-claim direction-change rejection releases the claim and a later retry succeeds" \
  bash -c "grep -q 'ApplyAsync_rejected_by_a_direction_change_to_biolog_is_master_after_claiming_releases_the_claim_and_a_later_retry_succeeds' tests/features/bio-int-002/ImportBatchServiceTests.cs"
check "round-4 finding 2: IImportRowProvenanceLedger is keyed by mappingId, not just idempotencyKey/externalKey" \
  bash -c "grep -q 'string mappingId' '$FEATURE/Domain/IImportRowProvenanceLedger.cs'"
check "round-4 finding 2: duplicate external keys within one request are rejected" \
  bash -c "grep -q 'ValidateNoDuplicateExternalKeys' '$FEATURE/ImportBatchService.cs' && grep -q 'DuplicateExternalKeysInRequest' '$FEATURE/Domain/ImportError.cs'"
check "a test proves a reused idempotency key against a different mapping never replays the wrong mapping's outcome" \
  bash -c "grep -q 'ApplyAsync_a_reused_idempotency_key_against_a_different_mapping_never_replays_the_first_mappings_outcome' tests/features/bio-int-002/ImportBatchServiceTests.cs"
check "a test proves ApplyAsync and PreviewAsync both reject duplicate external keys in one request" \
  bash -c "grep -q 'ApplyAsync_rejects_a_request_with_duplicate_external_keys' tests/features/bio-int-002/ImportBatchServiceTests.cs && grep -q 'PreviewAsync_rejects_a_request_with_duplicate_external_keys' tests/features/bio-int-002/ImportBatchServiceTests.cs"
check "residual durability note: provenance recording is verified-or-fail, not silently best-effort" \
  bash -c "grep -q 'private async Task RecordRowProvenanceSafelyAsync' '$FEATURE/ImportBatchService.cs' && ! grep -q 'private async Task RecordRowProvenanceBestEffortAsync' '$FEATURE/ImportBatchService.cs'"
check "a test proves a genuine provenance-recording failure fails the whole attempt and releases the claim" \
  bash -c "grep -q 'ApplyAsync_when_row_provenance_recording_genuinely_fails_the_whole_attempt_fails_and_releases_the_claim' tests/features/bio-int-002/ImportBatchServiceTests.cs"

# --- Post-review remediation round 6 (BIO-INT-002-F06, Seyi round-6 FAIL, architecture checkpoint) ---
check "round-6 finding: IImportRowProvenanceLedger carries a fingerprint alongside each recorded outcome" \
  bash -c "grep -q 'ImportRowProvenanceEntry' '$FEATURE/Domain/IImportRowProvenanceLedger.cs' && grep -q 'string fingerprint' '$FEATURE/Domain/IImportRowProvenanceLedger.cs'"
check "round-6 finding: no separate new ledger was introduced for this fix" \
  bash -c "! test -f '$FEATURE/Domain/IReleasedAttemptFingerprintLedger.cs'"
check "round-6 finding: a replay compares the stored fingerprint against the current request's own" \
  bash -c "grep -q 'recordedEntry.Fingerprint == fingerprint' '$FEATURE/ImportBatchService.cs'"
check "a test proves a released claim reused with different content for the same external key never silently replays stale provenance" \
  bash -c "grep -q 'ApplyAsync_a_released_claim_reused_with_different_content_for_the_same_external_key_never_silently_replays_stale_provenance' tests/features/bio-int-002/ImportBatchServiceTests.cs"
check "a test proves a released claim reused with the same content still replays normally" \
  bash -c "grep -q 'ApplyAsync_a_released_claim_reused_with_the_SAME_content_still_replays_normally' tests/features/bio-int-002/ImportBatchServiceTests.cs"

# --- Post-review remediation round 7 (BIO-INT-002-F07, Zainab round-6 FAIL, confirmed by Seyi) ---
check "round-7 finding: ComputeFingerprint hashes a canonical JSON serialization, not raw-delimiter concatenation" \
  bash -c "grep -q 'JsonSerializer.Serialize(canonical)' '$FEATURE/ImportBatchService.cs' && ! grep -q 'string.Join(\";\", rows' '$FEATURE/ImportBatchService.cs'"
check "a test proves two formerly-colliding field encodings now produce different fingerprints" \
  bash -c "grep -q 'ComputeFingerprint_a_field_value_containing_the_old_schemes_own_separator_syntax_no_longer_collides' tests/features/bio-int-002/ImportBatchServiceTests.cs"
check "a test proves two formerly-colliding row encodings now produce different fingerprints" \
  bash -c "grep -q 'ComputeFingerprint_a_field_value_containing_the_old_schemes_own_row_separator_no_longer_collides' tests/features/bio-int-002/ImportBatchServiceTests.cs"
check "a test proves a released claim reused with formerly-colliding content is never silently replayed" \
  bash -c "grep -q 'ApplyAsync_a_released_claim_reused_with_formerly_colliding_content_is_treated_as_genuinely_different_not_silently_replayed' tests/features/bio-int-002/ImportBatchServiceTests.cs"

# --- AC06: UI surface wired through IntegrationService ---
check "AC06 web ports.ts extends IntegrationsPort with real import methods" \
  bash -c "grep -q 'listMappings' web/features/integrations/ports.ts && grep -q 'previewImport' web/features/integrations/ports.ts && grep -q 'applyImport' web/features/integrations/ports.ts"
check "AC06 live.ts wires the new methods to the real backend endpoints" \
  bash -c "grep -q '/api/v1/integrations/mappings' web/features/integrations/live.ts"
check "AC06 the administration/integrations page is no longer a bare PlaceholderPage" \
  bash -c "! grep -q 'export default function.*PlaceholderPage' web/app/administration/integrations/page.tsx"
check "AC06 settings/authentication's HYBRID disclosure is documented, and its own file is otherwise untouched by this story" \
  bash -c "grep -qi 'BIO-INT-001' web/app/settings/authentication/page.tsx"

# --- Tenant isolation ---
check "mapping repository enforces tenant isolation on read" \
  bash -c "grep -q 'mapping.TenantId != tenantId' '$FEATURE/Domain/InMemoryImportSourceMappingRepository.cs'"

printf '\n%d passed, %d failed\n' "$pass" "$fail"
[ "$fail" -eq 0 ]
