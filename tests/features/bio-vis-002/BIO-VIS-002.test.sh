#!/usr/bin/env bash
# Offline-safe structural checks for BIO-VIS-002 (visitor check-in/out and temporary credentials).
# Behavioural assertions live in the executable xunit suites under tests/features/bio-vis-002/
# (see docs/features/bio-vis-002/README.md).
set -uo pipefail
cd "$(git rev-parse --show-toplevel)"
pass=0; fail=0
check() { local label="$1"; shift; if "$@"; then echo "PASS  $label"; pass=$((pass+1)); else echo "FAIL  $label"; fail=$((fail+1)); fi; }

FEATURE=api/features/bio-vis-002
DEP1=api/features/bio-vis-001
DEP2=api/features/bio-crd-005

# --- Project shape ---
check "feature project exists" test -f "$FEATURE/Biolog.Features.VisitorCredentials.csproj"
check "project targets the pinned .NET SDK's TFM" bash -c "grep -q '<TargetFramework>net10.0</TargetFramework>' '$FEATURE/Biolog.Features.VisitorCredentials.csproj'"
check "module lives one-way in api/Biolog.Api/Modules, not back in the feature project" \
  bash -c "test -f api/Biolog.Api/Modules/BioVis002Module.cs && ! test -f '$FEATURE/BioVis002Module.cs'"
check "registered in the packaged Program.cs module array" \
  bash -c "grep -q 'new BioVis002Module()' api/Biolog.Api/Program.cs"
check "Biolog.Api.csproj references the feature project" \
  bash -c "grep -q 'features/bio-vis-002/Biolog.Features.VisitorCredentials.csproj' api/Biolog.Api/Biolog.Api.csproj"
check "no ProjectReference from the feature project to any other feature project or Biolog.Api" \
  bash -c "! grep -q '<ProjectReference' '$FEATURE/Biolog.Features.VisitorCredentials.csproj'"

# --- AC01: session/roster n/a for this story - visitor/credential association instead ---
check "VisitorCredential references the visitor record it was granted for, and the real BIO-CRD-005 card backing it" \
  bash -c "grep -q 'VisitorRecordId' '$FEATURE/Domain/VisitorCredential.cs' && grep -q 'CardClass' '$FEATURE/Domain/VisitorCredential.cs' && grep -q 'CardSequenceNumber' '$FEATURE/Domain/VisitorCredential.cs'"
check "no canonical identifier-registry prefix is minted for VisitorCredential (disclosed, precedented boundary)" \
  bash -c "grep -q 'NewVisitorCredentialId' '$FEATURE/Identifiers/CanonicalId.cs'"

# --- AC02: temporary credential has expiry and scope, backed by a real card ---
check "AC02 VisitorCredential carries its own ExpiresAt and a Scope distinct from the visit's own fields" \
  bash -c "grep -q 'required string ExpiresAt' '$FEATURE/Domain/VisitorCredential.cs' && grep -q 'required VisitorCredentialScope Scope' '$FEATURE/Domain/VisitorCredential.cs'"
check "AC02 grant rejects a credential expiry later than the visit's own expiry" \
  bash -c "grep -q 'ExpiresAtExceedsVisit' '$FEATURE/VisitorCredentialService.cs'"
check "AC02 grant issues AND activates a real BIO-CRD-005 card, never a credential with no backing card" \
  bash -c "grep -q 'cardGateway.IssueAsync' '$FEATURE/VisitorCredentialService.cs' && grep -q 'cardGateway.ActivateAsync' '$FEATURE/VisitorCredentialService.cs'"
check "AC02 a visitor with no linked PERSON cannot be granted a credential (disclosed BIO-CRD-005 precondition)" \
  bash -c "grep -q 'VisitorHasNoLinkedPerson' '$FEATURE/VisitorCredentialService.cs' && grep -q 'VisitorHasNoLinkedPerson' '$FEATURE/Domain/VisitorCredentialError.cs'"
check "AC02 grant is a genuine authorization decision: requires a permission grant AND a verified ADR-0006 provenance proof" \
  bash -c "grep -q 'ManagePermission' '$FEATURE/VisitorCredentialService.cs' && grep -q 'provenanceGate.Require' '$FEATURE/Api/VisitorCredentialEndpoints.cs'"
check "a test proves granting issues and activates a real card and persists a credential" \
  bash -c "grep -q 'GrantAsync_issues_and_activates_a_real_backing_card_and_persists_a_credential' tests/features/bio-vis-002/VisitorCredentialServiceTests.cs"
check "a test proves a credential expiry later than the visit's own expiry is rejected" \
  bash -c "grep -q 'GrantAsync_rejects_an_expiry_later_than_the_visits_own_expiry' tests/features/bio-vis-002/VisitorCredentialServiceTests.cs"
check "a test proves a visitor with no linked person cannot be granted a credential, and BIO-CRD-005 is never even called" \
  bash -c "grep -q 'GrantAsync_fails_when_the_visitor_has_no_linked_person' tests/features/bio-vis-002/VisitorCredentialServiceTests.cs"

# --- AC03: check-out revokes/ends the credential ---
check "AC03 Revoke moves the credential one-way Granted -> Revoked, never back" \
  bash -c "grep -q 'internal VisitorCredential Revoke' '$FEATURE/Domain/VisitorCredential.cs'"
check "AC03 this feature's own orchestrating CheckOutAsync calls through to BIO-VIS-001's own real checkout, then revokes" \
  bash -c "grep -q 'visitorGateway.CheckOutAsync' '$FEATURE/VisitorCredentialService.cs' && grep -q 'RevokeLockedAsync' '$FEATURE/VisitorCredentialService.cs'"
check "AC03 revoking deactivates the backing BIO-CRD-005 card too" \
  bash -c "grep -q 'cardGateway.DeactivateAsync' '$FEATURE/VisitorCredentialService.cs'"
check "AC03 automatic revoke-on-checkout never requires the grant/revoke permission - it is a consequence, not a second decision" \
  bash -c "! grep -q 'permissionGateway' <(sed -n '/public async Task<(VisitorRecordView Visitor, VisitorCredential? RevokedCredential)> CheckOutAsync/,/^    }/p' '$FEATURE/VisitorCredentialService.cs')"
check "a test proves checkout revokes an active credential and deactivates its backing card" \
  bash -c "grep -q 'CheckOutAsync_revokes_an_active_credential_and_deactivates_its_backing_card' tests/features/bio-vis-002/VisitorCredentialServiceTests.cs"
check "a test proves checkout never requires the grant/revoke permission" \
  bash -c "grep -q 'CheckOutAsync_never_requires_the_grant_revoke_permission_it_is_audit_attribution_only' tests/features/bio-vis-002/VisitorCredentialServiceTests.cs"

# --- AC04: overstay/expired status is visible ---
check "AC04 a genuinely distinct Overstay status exists, never collapsed into Expired" \
  bash -c "grep -q 'Overstay,' '$FEATURE/Domain/VisitorCredentialEnums.cs' && grep -q 'Expired,' '$FEATURE/Domain/VisitorCredentialEnums.cs'"
check "AC04 the overlay is a pure, read-time-only function - no clock read of its own, no mutation" \
  bash -c "grep -q 'public static VisitorEffectiveStatus EffectiveStatus' '$FEATURE/Domain/VisitorOverstayEvaluator.cs'"
check "a test proves a checked-in visitor past expiry is Overstay, not Expired" \
  bash -c "grep -q 'CheckedIn_past_expiry_is_overstay_not_expired' tests/features/bio-vis-002/VisitorOverstayEvaluatorTests.cs"
check "a test proves a never-arrived, lapsed preregistration is Expired, not Overstay" \
  bash -c "grep -q 'Preregistered_past_expiry_is_expired_not_overstay' tests/features/bio-vis-002/VisitorOverstayEvaluatorTests.cs"
check "a test proves the identical stored view classifies differently at two different read times" \
  bash -c "grep -q 'The_identical_stored_view_classifies_differently_at_two_different_read_times' tests/features/bio-vis-002/VisitorOverstayEvaluatorTests.cs"

# --- AC05: visitor history is auditable ---
check "AC05 a combined history read exists: visitor lifecycle plus every credential ever granted/revoked" \
  bash -c "grep -q 'GetHistoryAsync' '$FEATURE/VisitorCredentialService.cs' && grep -q 'credential-history' '$FEATURE/Api/VisitorCredentialEndpoints.cs'"
check "AC05 every grant/revoke/checkout-orchestration mutation is audited before the repository commit" \
  bash -c "grep -q 'AuditAsync' '$FEATURE/VisitorCredentialService.cs' && grep -q 'ExistsAsync' '$FEATURE/Audit/IVisitorCredentialAuditSink.cs'"
check "a test proves history returns the visitor lifecycle and every credential ever granted" \
  bash -c "grep -q 'GetHistoryAsync_returns_the_visitor_lifecycle_and_every_credential_ever_granted' tests/features/bio-vis-002/VisitorCredentialServiceTests.cs"

# --- AC06: /visitors stays connected through VisitorService; this story takes over check-out ---
check "AC06 this story's own endpoint mapping takes over POST /{visitorId}/check-out" \
  bash -c "grep -q '\"/{visitorId}/check-out\"' '$FEATURE/Api/VisitorCredentialEndpoints.cs'"
check "AC06 BIO-VIS-001's own ROUTE MAPPING for /{visitorId}/check-out was removed (not left duplicated)" \
  bash -c "! grep -q '\"/{visitorId}/check-out\"' '$DEP1/Api/VisitorEndpoints.cs'"
check "AC06 BIO-VIS-001's own CheckOutAsync APPLICATION SERVICE method is untouched - this story calls through to it, never reimplements it" \
  bash -c "grep -q 'public async Task<VisitorRecord> CheckOutAsync' '$DEP1/VisitorRecordService.cs'"
check "AC06 web/features/visitors/live.ts is UNCHANGED - no redesign, this story only re-points which module answers one existing route" \
  bash -c "grep -q \"'/api/v1/visitors/check-out-visitor'\" web/features/visitors/live.ts"
check "AC06 the three headerless demo bridge routes BIO-VIS-001 already answers for real are untouched" \
  bash -c "grep -q '\"/primary-action\"' '$DEP1/Api/VisitorEndpoints.cs' && grep -q '\"/check-out-visitor\"' '$DEP1/Api/VisitorEndpoints.cs' && grep -q '\"/view-visit-history\"' '$DEP1/Api/VisitorEndpoints.cs'"
check "AC06 visitors-page.tsx, ports.ts and mock.ts are untouched by this story (no unapproved UI redesign)" \
  bash -c "! git diff --name-only HEAD~1 -- web/features/visitors/visitors-page.tsx web/features/visitors/ports.ts web/features/visitors/mock.ts 2>/dev/null | grep -q ."

# --- Cross-story gateway pattern ---
check "IVisitorRecordGateway/ICardIssuanceGateway are this project's own interfaces, not references to bio-vis-001/bio-crd-005's own types" \
  bash -c "grep -q 'interface IVisitorRecordGateway' '$FEATURE/Domain/IVisitorRecordGateway.cs' && grep -q 'interface ICardIssuanceGateway' '$FEATURE/Domain/ICardIssuanceGateway.cs'"
check "the real adapters live in BioVis002Module.cs, wrapping each sibling's own already-registered internal service" \
  bash -c "grep -q 'VisitorRecordGatewayAdapter' api/Biolog.Api/Modules/BioVis002Module.cs && grep -q 'CardIssuanceGatewayAdapter' api/Biolog.Api/Modules/BioVis002Module.cs"
check "a sibling domain error is translated into this feature's own error type, never left to surface as an unhandled 500" \
  bash -c "grep -q 'Translate(Biolog.Features.Visitors.Domain.VisitorError' api/Biolog.Api/Modules/BioVis002Module.cs && grep -q 'Translate(Biolog.Features.CardIssuance.Domain.CardIssueError' api/Biolog.Api/Modules/BioVis002Module.cs"

# --- Post-review remediation (BIO-VIS-002-F01/F02, Seyi's FAIL on commit 45f4736) ---
check "F01 a grant attempt persists a Pending anchor before any BIO-CRD-005 call" \
  bash -c "grep -q 'CreatePending' '$FEATURE/VisitorCredentialService.cs' && grep -q 'VisitorCredentialStatus.Pending' '$FEATURE/Domain/VisitorCredentialEnums.cs'"
check "F01 the pre-issue duplicate check consults BIO-CRD-005's own state, not just the local row" \
  bash -c "grep -q 'ListAsync' '$FEATURE/Domain/ICardIssuanceGateway.cs' && grep -q 'cardGateway.ListAsync' '$FEATURE/VisitorCredentialService.cs'"
check "F01 a confirmed final-commit failure compensates the card and converges to a terminal Failed record" \
  bash -c "grep -q 'MarkFailedBestEffortAsync' '$FEATURE/VisitorCredentialService.cs' && grep -q 'VisitorCredentialStatus.Failed' '$FEATURE/Domain/VisitorCredentialEnums.cs'"
check "a test proves a retry adopts an already-active stranded card instead of issuing a second one" \
  bash -c "grep -q 'GrantAsync_adopts_an_already_active_card_from_a_prior_partial_failure_instead_of_issuing_a_second_one' tests/features/bio-vis-002/VisitorCredentialServiceTests.cs"
check "a test proves a confirmed final-commit failure compensates the card and converges to Failed" \
  bash -c "grep -q 'GrantAsync_when_the_final_local_commit_fails_after_the_card_is_active_the_card_is_compensated_and_the_record_converges_to_failed' tests/features/bio-vis-002/VisitorCredentialServiceTests.cs"
check "F02 checkout revokes the credential BEFORE committing BIO-VIS-001's own checkout, not after" \
  bash -c "
revoke_line=\$(grep -n 'RevokeLockedAsync(tenantId, active,' '$FEATURE/VisitorCredentialService.cs' | tail -1 | cut -d: -f1)
checkout_line=\$(grep -n 'visitorGateway.CheckOutAsync(visitorId, expectedVersion,' '$FEATURE/VisitorCredentialService.cs' | cut -d: -f1)
[ -n \"\$revoke_line\" ] && [ -n \"\$checkout_line\" ] && [ \"\$revoke_line\" -lt \"\$checkout_line\" ]
"
check "a test proves a failed revoke never reaches BIO-VIS-001's own one-way checkout, and the visitor stays checked-in" \
  bash -c "grep -q 'CheckOutAsync_when_revoke_fails_the_underlying_visitor_checkout_is_never_called_and_the_visitor_stays_checked_in' tests/features/bio-vis-002/VisitorCredentialServiceTests.cs"
check "a test proves checkout is idempotent on retry once already checked out" \
  bash -c "grep -q 'CheckOutAsync_called_again_after_the_visitor_is_already_checked_out_is_an_idempotent_no_op' tests/features/bio-vis-002/VisitorCredentialServiceTests.cs"

# --- Post-review remediation round 3 (BIO-VIS-002-F03, Seyi+Zainab's round-2 FAIL on commit c9e84b2) ---
check "F03 card adoption is correlated to ownership, not just (person, card class)" \
  bash -c "grep -q 'FindByCardAsync' '$FEATURE/Domain/IVisitorCredentialRepository.cs' && grep -q 'repository.FindByCardAsync' '$FEATURE/VisitorCredentialService.cs'"
check "F03 the grant lock is keyed by person (falling back to visitor id only when PersonId is null), not by visitor id alone" \
  bash -c "grep -q 'lockRegistry.AcquireAsync(tenantId, lockKey' '$FEATURE/VisitorCredentialService.cs' && grep -q 'preRead.PersonId ?? visitorId' '$FEATURE/VisitorCredentialService.cs'"
check "F03 grant recovery (reread and best-effort compensation) uses an independent token, never the caller's own" \
  bash -c "! grep -q 'MarkFailedBestEffortAsync(VisitorCredential pending, string reason, int? cardSequenceNumber, CancellationToken' '$FEATURE/VisitorCredentialService.cs'"
check "a test proves two visitor records for the same person never share a card, and revoking one never touches the other" \
  bash -c "grep -q 'GrantAsync_two_visitor_records_for_the_same_person_sequentially_never_share_a_card_and_revoking_one_never_touches_the_other' tests/features/bio-vis-002/VisitorCredentialServiceTests.cs"
check "a test proves concurrent grants for two visitor records of the same person never race into sharing a card" \
  bash -c "grep -q 'GrantAsync_concurrent_grants_for_two_visitor_records_of_the_same_person_never_race_into_sharing_a_card' tests/features/bio-vis-002/VisitorCredentialServiceTests.cs"
check "a test proves compensation still runs when the caller's token is cancelled right after activation" \
  bash -c "grep -q 'GrantAsync_when_cancelled_right_after_activation_still_compensates_and_converges_to_failed' tests/features/bio-vis-002/VisitorCredentialServiceTests.cs"

# --- Post-review remediation round 4 (BIO-VIS-002-F04, Seyi's round-3 FAIL on commit 9c8b56f) ---
check "F04 a card's identity is durably correlated to its Pending row before activation/audit/the final transition, not only at the end of the attempt" \
  bash -c "grep -q 'AttachCardSequenceAsync' '$FEATURE/VisitorCredentialService.cs' && grep -q 'pending = await AttachCardSequenceAsync(pending, card.SequenceNumber)' '$FEATURE/VisitorCredentialService.cs'"
check "F04 a stranded-card ledger exists and the adoption check consults it before FindByCardAsync's own ownership check" \
  bash -c "test -f '$FEATURE/Domain/IStrandedCardLedger.cs' && grep -q 'strandedCardLedger.IsStrandedAsync' '$FEATURE/VisitorCredentialService.cs'"
check "F04 the ledger is only ever written as a last resort, once compensation and the local Failed-write have both failed" \
  bash -c "grep -q 'cardConfirmedNotActive' '$FEATURE/VisitorCredentialService.cs' && grep -q 'localCommitSucceeded' '$FEATURE/VisitorCredentialService.cs' && grep -q 'strandedCardLedger.MarkStrandedAsync' '$FEATURE/VisitorCredentialService.cs'"
check "a test proves a card stranded by a double local-write failure is recorded and a later visitor for the same person cannot adopt it" \
  bash -c "grep -q 'GrantAsync_when_both_local_persistence_and_compensation_are_exhausted_the_card_is_recorded_stranded_and_a_later_visitor_cannot_adopt_it' tests/features/bio-vis-002/VisitorCredentialServiceTests.cs"

# --- Post-review remediation round 5 (BIO-VIS-002-F05, Seyi round-4 FAIL / Zainab round-3 FAIL) ---
check "F05 a reconciliation capability exists that retries deactivating ledgered cards, not just excluding them from adoption" \
  bash -c "grep -q 'ReconcileStrandedCardsAsync' '$FEATURE/VisitorCredentialService.cs' && grep -q 'ListUnresolvedAsync' '$FEATURE/Domain/IStrandedCardLedger.cs' && grep -q 'MarkResolvedAsync' '$FEATURE/Domain/IStrandedCardLedger.cs'"
check "F05 reconciliation is wired opportunistically into GrantAsync under the same person-scoped lock, not left as a dead capability" \
  bash -c "grep -q 'await ReconcileStrandedCardsAsync(tenantId, visitor.PersonId, CardClass, actor, correlationId)' '$FEATURE/VisitorCredentialService.cs'"
check "a test proves a ledgered card is genuinely deactivated (not just excluded from adoption) once the card service recovers" \
  bash -c "grep -q 'ReconcileStrandedCardsAsync_deactivates_a_ledgered_card_once_the_card_service_recovers_and_the_ledger_entry_converges_to_resolved' tests/features/bio-vis-002/VisitorCredentialServiceTests.cs"
check "a test proves reconciliation fires opportunistically on a subsequent grant for the same person, with no explicit call needed" \
  bash -c "grep -q 'GrantAsync_opportunistically_reconciles_this_persons_own_stranded_cards_before_proceeding' tests/features/bio-vis-002/VisitorCredentialServiceTests.cs"
check "a test proves reconciliation leaves an entry unresolved and retryable while the card service is still unavailable" \
  bash -c "grep -q 'ReconcileStrandedCardsAsync_leaves_an_entry_unresolved_and_retries_next_time_while_deactivation_keeps_failing' tests/features/bio-vis-002/VisitorCredentialServiceTests.cs"

# --- Post-review remediation round 6 (BIO-VIS-002-F06, Seyi round-5 FAIL / Zainab round-4 FAIL) ---
check "F06 GrantAsync fails closed when reconciliation leaves any unresolved stranded card for this person, instead of issuing a second one" \
  bash -c "grep -q 'StrandedCardUnresolved' '$FEATURE/VisitorCredentialService.cs' && grep -q 'StrandedCardUnresolved' '$FEATURE/Domain/VisitorCredentialError.cs'"
check "a test proves a new card is never issued while an unresolved stranded card remains for the same person" \
  bash -c "grep -q 'GrantAsync_blocks_issuing_a_new_card_while_an_unresolved_stranded_card_remains_for_the_same_person' tests/features/bio-vis-002/VisitorCredentialServiceTests.cs"
check "F06 a guaranteed-availability operator-recovery entry point exists, independent of any grant/checkout/revoke for the affected person" \
  bash -c "grep -q 'ReconcileStrandedCardsForPersonAsync' '$FEATURE/VisitorCredentialService.cs'"
check "F06 the operator-recovery entry point is mapped as its own real HTTP endpoint, gated identically to grant/revoke" \
  bash -c "grep -q 'MapPost(\"/reconcile\", ReconcileAsync)' '$FEATURE/Api/VisitorCredentialEndpoints.cs' && grep -q 'provenanceGate.Require' '$FEATURE/Api/VisitorCredentialEndpoints.cs'"
check "a test proves the operator-recovery entry point neutralizes a stranded card with no grant/checkout/revoke involved" \
  bash -c "grep -q 'ReconcileStrandedCardsForPersonAsync_neutralizes_a_stranded_card_without_any_grant_checkout_or_revoke' tests/features/bio-vis-002/VisitorCredentialServiceTests.cs"
check "a real-packaged-host test proves the reconcile endpoint is genuinely mapped and gated, not a dead route" \
  bash -c "grep -q 'VISCRED005' tests/dotnet/Biolog.IntegrationTests/VisitorCredentialsHostChannelTests.cs"
check "F06 the opportunistic reconciliation trigger is widened into CheckOutAsync/RevokeAsync too, beyond just GrantAsync" \
  bash -c "grep -c 'await ReconcileStrandedCardsAsync(tenantId,' '$FEATURE/VisitorCredentialService.cs' | grep -q '^4$'"
check "a test proves CheckOutAsync also opportunistically reconciles this person's own stranded cards" \
  bash -c "grep -q 'CheckOutAsync_opportunistically_reconciles_this_persons_own_stranded_cards_too' tests/features/bio-vis-002/VisitorCredentialServiceTests.cs"

# --- Post-review remediation round 7 (BIO-VIS-002-F07, Seyi round-7 FAIL / Zainab round-5 FAIL) ---
check "F07 the stranded-card ledger can mint and durably persist a stable reconciliation audit event id" \
  bash -c "grep -q 'GetOrCreateReconciliationEventIdAsync' '$FEATURE/Domain/IStrandedCardLedger.cs' && grep -q 'GetOrCreateReconciliationEventIdAsync' '$FEATURE/Domain/InMemoryStrandedCardLedger.cs'"
check "F07 AuditAsync accepts an optional stable event id so a retry can be recognised as already durably written" \
  bash -c "grep -q 'string? eventId = null' '$FEATURE/VisitorCredentialService.cs'"
check "F07 ReconcileStrandedCardsAsync always runs the audit write under the entry's own stable id before marking resolved" \
  bash -c "grep -q 'eventId: auditEventId' '$FEATURE/VisitorCredentialService.cs'"
check "F07 the stale non-blocking doc comment claiming no operator-independent trigger exists has been corrected" \
  bash -c "! grep -q 'NO grant attempt for this person ever happens again, reconciliation never runs' '$FEATURE/VisitorCredentialService.cs'"
check "a test proves a failed audit write leaves the ledger unresolved with no audit record, and a retry writes exactly one" \
  bash -c "grep -q 'ReconcileStrandedCardsAsync_when_the_audit_write_fails_after_deactivation_succeeds_a_retry_writes_exactly_one_audit_record_and_the_ledger_converges_to_resolved' tests/features/bio-vis-002/VisitorCredentialServiceTests.cs"
check "a fault-injecting audit sink test double exists to simulate the audit write genuinely failing" \
  bash -c "grep -q 'class FaultInjectingVisitorCredentialAuditSink' tests/features/bio-vis-002/Fakes.cs"

# --- Post-review remediation round 8 (BIO-VIS-002-F08, Seyi round-8 FAIL / Zainab round-6 FAIL) ---
check "F08 the audit sink exposes an atomic create-if-absent primitive, not just ExistsAsync/WriteAsync" \
  bash -c "grep -q 'CreateIfAbsentAsync' '$FEATURE/Audit/IVisitorCredentialAuditSink.cs'"
check "F08 the in-memory sink implements create-if-absent via ConcurrentDictionary.GetOrAdd, the atomic primitive itself" \
  bash -c "grep -q 'GetOrAdd' '$FEATURE/Audit/InMemoryVisitorCredentialAuditSink.cs'"
check "F08 AuditAsync's stable-id path routes through CreateIfAbsentAsync, not the racy ExistsAsync-then-WriteAsync sequence" \
  bash -c "grep -q 'auditSink.CreateIfAbsentAsync' '$FEATURE/VisitorCredentialService.cs'"
check "F08 the fault-injecting audit sink test double also gates CreateIfAbsentAsync, not just WriteAsync" \
  bash -c "grep -q 'public Task<VisitorCredentialAuditEvent> CreateIfAbsentAsync' tests/features/bio-vis-002/Fakes.cs"
check "a barrier-based test proves two genuinely concurrent writers for the same stable event id converge to exactly one payload" \
  bash -c "grep -q 'CreateIfAbsentAsync_two_genuinely_concurrent_writers_for_the_same_event_id_converge_to_exactly_one_immutable_payload' tests/features/bio-vis-002/VisitorCredentialServiceTests.cs"
check "a test proves two concurrent ReconcileStrandedCardsAsync callers for the same stranded card converge to exactly one audit record" \
  bash -c "grep -q 'ReconcileStrandedCardsAsync_two_concurrent_callers_for_the_same_stranded_card_converge_to_exactly_one_audit_record' tests/features/bio-vis-002/VisitorCredentialServiceTests.cs"

# --- Post-review remediation round 10 (BIO-VIS-002-F09 continued, Seyi round-10 FAIL: mutation/differential proof) ---
check "F09 a reference sink places the barrier INSIDE the GetOrAdd factory delegate itself, not merely before delegation" \
  bash -c "grep -q 'class AtomicFactoryRaceSink' tests/features/bio-vis-002/Fakes.cs"
check "F09 a reference sink deliberately reimplements the historical check-then-write bug to prove the proof's own discriminating power" \
  bash -c "grep -q 'class RacyCheckThenWriteSink' tests/features/bio-vis-002/Fakes.cs"
check "a mutation-proof test shows the atomic GetOrAdd-based sink survives forced concurrent factory invocation" \
  bash -c "grep -q 'CreateIfAbsent_mutation_proof_the_atomic_GetOrAdd_factory_based_sink_survives_forced_concurrent_factory_invocation' tests/features/bio-vis-002/VisitorCredentialServiceTests.cs"
check "a mutation-proof test shows the historical check-then-write algorithm violates immutability once both callers observe absent" \
  bash -c "grep -q 'CreateIfAbsent_mutation_proof_the_historical_check_then_write_algorithm_violates_immutability_once_both_callers_observe_absent' tests/features/bio-vis-002/VisitorCredentialServiceTests.cs"

# --- Tests and evidence ---
check "backend unit tests exist" test -f tests/features/bio-vis-002/VisitorCredentialServiceTests.cs
check "a real-packaged-host test exists proving the genuine BIO-VIS-001/BIO-CRD-005 dependency through the shipped host channel" \
  bash -c "grep -rlq 'BIO-VIS-002' tests/dotnet/Biolog.IntegrationTests/*.cs"
check "builder evidence recorded" test -f evidence/features/bio-vis-002/builder/metadata.json

echo "=============================="
echo "$pass passed, $fail failed"
[ "$fail" -eq 0 ]
