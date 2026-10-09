using Biolog.Features.CardLifecycle.Audit;
using Biolog.Features.CardLifecycle.Domain;
using Biolog.Features.CardLifecycle.Identifiers;
using Biolog.Features.CardLifecycle.Tenancy;

namespace Biolog.Features.CardLifecycle;

/// <summary>
/// Application service for BIO-CRD-006. Applies ADR-0003 Decision 7's audit-atomicity pattern
/// the same way BIO-CRD-005's own <c>CardIssueService</c> does, including that story's own
/// BIO-CRD-005-QA-01 lesson from the start: every mutating method audits BEFORE committing its
/// own <see cref="CardLifecycleRecord"/>, never after.
///
/// <b>Why report/recover never touch BIO-CRD-005's own <c>CardIssueStatus</c>.</b> See
/// <see cref="CardLockState"/>'s own header - that story's <c>Deactivated</c> status is a
/// one-way terminal state there, which would make AC02's "recovery may reactivate" impossible
/// if report/recover flipped it. This service's own <see cref="CardLockState"/> is the SOLE,
/// authoritative gate <see cref="CheckPresentationAsync"/> and
/// <see cref="IntakePresentationEventAsync"/> consult - <see cref="ICardIssueGateway"/> is used
/// only to validate a card issue exists, to issue/activate a replacement (AC03/AC04), and for a
/// best-effort secondary sync of BIO-CRD-005's own record during AC04's PERMANENT sweep (where,
/// unlike report/recover, there is no tension: both this service's own
/// <see cref="CardLockState.PermanentlyRevoked"/> and BIO-CRD-005's own
/// <c>CardIssueStatus.Deactivated</c> are terminal, so keeping both in sync there is safe).
///
/// <b>BIO-CRD-006-QA-01 (Chioma's acceptance review round 1).</b> <see cref="ActivateReplacementAsync"/>
/// sweeps EVERY earlier sequence number to <see cref="CardLockState.PermanentlyRevoked"/> -
/// each record's own audit written and confirmed durable before that record's own commit,
/// per <see cref="RevokePermanentlyAsync"/> - and only once the WHOLE sweep has fully
/// completed does it call <see cref="ICardIssueGateway.ActivateAsync"/> to actually activate
/// the replacement. The original order (activate first, sweep after) let a later revoke in the
/// sweep fail while the replacement was already observably Active, leaving an earlier card
/// still <see cref="CardLockState.Locked"/>/<see cref="CardLockState.Recovered"/> - reachable
/// by <see cref="RecoverAsync"/> - or simply absent from <see cref="GetDenyListAsync"/>, a real
/// violation of AC04/AC05 for the whole failure interval, not merely until a caller retries.
/// Sweeping BEFORE activating makes "the replacement is Active" a true invariant that every
/// earlier card is already permanently revoked, for every OBSERVABLE state, not only the
/// eventual end state.
///
/// <b>AC04's sweep is end-to-end idempotent.</b> Skipping any record already
/// <see cref="CardLockState.PermanentlyRevoked"/> (see <see cref="RevokePermanentlyAsync"/>)
/// combined with <see cref="ICardIssueGateway.ActivateAsync"/>'s own Idempotency-Key replay
/// makes the WHOLE operation safe to retry with the same Idempotency-Key as many times as
/// needed: a retry after a partial sweep failure only re-attempts the specific earlier cards
/// not yet revoked, then proceeds to activation once the sweep is complete.
///
/// <b>BIO-CRD-006-QA-02 (Chioma's acceptance review round 2).</b> QA-01's fix alone still let a
/// BAD replacement activation request (a stale/missing <c>expected_version</c>, or a
/// replacement that is not actually <see cref="CardIssueLifecycleStatus.Issued"/>) run the
/// whole earlier-card revocation sweep before <see cref="ICardIssueGateway.ActivateAsync"/>
/// finally rejected it - an invalid request could permanently destroy earlier cards'
/// recoverability for nothing, with no replacement ever activated (AC02/security DoS).
/// <see cref="ActivateReplacementAsync"/> now: (1) reserves the caller's own Idempotency-Key at
/// THIS story's own layer via <see cref="IIdempotencyStore"/> - fingerprinted on
/// (person, card class, sequence, expected_version) - before anything else, so a genuine replay
/// short-circuits before any side effect and a conflicting reuse is rejected immediately;
/// (2) reads the replacement's current snapshot and validates the SAME preconditions
/// BIO-CRD-005's own <c>ActivateAsync</c> would eventually validate at commit time (exists,
/// <c>expected_version</c> precondition, not already <see cref="CardIssueLifecycleStatus.Deactivated"/>)
/// - all BEFORE any revocation side effect; only once that dry-run passes does it (3) run the
/// full sweep (still strictly before activation, preserving QA-01's own fix), then (4) call the
/// real <see cref="ICardIssueGateway.ActivateAsync"/>, which remains the final, authoritative
/// commit (including its own Idempotency-Key handling as a second, independent safety net).
/// This dry-run cannot be a literal second call to BIO-CRD-005's own <c>ActivateAsync</c> - that
/// story has no "validate without committing" mode, and calling the real one twice would either
/// double-activate or contradict QA-01's own "not externally committed until the sweep
/// succeeds" rule; it is instead this project's own narrow re-validation of the same read-only
/// facts.
///
/// <b>BIO-CRD-006-QA-03 (Chioma's acceptance review round 3).</b> QA-02's dry-run closed the
/// "obviously bad request" gap, but not a genuine TIME-OF-CHECK-TO-TIME-OF-USE race: the dry-run
/// reads a snapshot, the sweep runs, and only THEN does the real, authoritative
/// <see cref="ICardIssueGateway.ActivateAsync"/> compare-and-set run against BIO-CRD-005's own,
/// independently-locked store. A concurrent mutation there between the read and that final
/// compare-and-set (e.g. someone calling BIO-CRD-005's own raw <c>/activate</c> or
/// <c>/deactivate</c> endpoint directly, bypassing this story entirely) can still make that final
/// call fail AFTER the sweep already ran - reproducing QA-01's own "revoked for nothing, no
/// replacement ever active" outcome, just via a narrower door. A true fix would need
/// BIO-CRD-005's own service to expose a genuine reserve/prepare primitive - which would mean
/// modifying that different, already-accepted story's own owned files, outside this project's
/// write scope and standing. Chioma's own remediation explicitly offers a second, equally valid
/// path: "or safely compensated" - taken here instead. <see cref="BeginRevocationAsync"/> moves
/// each earlier card to the new, non-terminal <see cref="CardLockState.RevocationPending"/> (see
/// that enum's own header) - still denied, still non-recoverable, so QA-01's own invariant still
/// holds throughout - recording the PRIOR state on the record itself. If the real
/// <see cref="ICardIssueGateway.ActivateAsync"/> call then fails, <see cref="CompensateRevocationAsync"/>
/// reverts every such record back to that prior state (or <see cref="CardLockState.Unlocked"/> if
/// it had none) - a real, durable, forward-only compensating transition, never a silent delete,
/// matching this codebase's own "no delete operation, ever" convention. If it succeeds instead,
/// <see cref="ConfirmRevocationAsync"/> moves each record on to the terminal
/// <see cref="CardLockState.PermanentlyRevoked"/>. This story's own Idempotency-Key completion is
/// recorded only once BOTH the activation and the confirmation sweep have fully succeeded, so a
/// retry after a confirmation-only failure resumes confirming rather than replaying a stale
/// result.
///
/// <b>BIO-OPS-003-AC04 (Seyi's acceptance run, Kemi's architectural ruling).</b> QA-01 through
/// QA-03 made the revocation sweep itself atomic/compensated, but neither this method nor
/// BIO-CRD-007's own <c>ActivateAfterProductionAsync</c> ever composed "production passed" AND
/// "earlier cards revoked" into one check - this is the only method that performs the real
/// revocation sweep, so it is also the only place that can enforce both together.
/// <see cref="ActivateReplacementAsync"/> now consults <see cref="IProductionStatusGateway"/>
/// right after the lock is acquired and before the sweep begins - a card whose latest production
/// job has not Passed throws <see cref="CardLifecycleError.ProductionNotPassed"/> and releases
/// the idempotency claim (same catch-all as every other precondition failure here), before any
/// earlier card is ever touched.
///
/// <b>BIO-OPS-003-AC04 round 2 (SY-OPS003-02, Kemi's ruling).</b> Round 1 closed the sequential
/// gap but not a genuine time-of-check-to-time-of-use race: this method's own
/// <c>CardLifecycleLockRegistry</c> and BIO-CRD-007's own <c>ProductionJobLockRegistry</c> are
/// fully independent locks, so a concurrent BIO-CRD-007 mutation could land between the
/// production-status read above and the real activation call below. <see cref="ICardActivationBarrier"/>
/// - a narrow seam onto a new, shared, host-layer lock BOTH features now acquire for the same
/// key - closes this: acquired right after the lock, held through the activation call
/// completing, keyed by the replacement's own sequence number.
///
/// <b>BIO-OPS-007-AC01 (Femi's concurrent-replacement hardening find).</b> The rounds above made
/// the sweep and the production/lock composition atomic, but never addressed two DISTINCT,
/// never-yet-activated replacements (e.g. sequence 2 and sequence 3, both Issued while sequence 1
/// was still Active) racing <see cref="ActivateReplacementAsync"/> concurrently. This story's own
/// <c>lockRegistry</c> (keyed by tenant/person/card class only) already serialises the two calls
/// completely - the race is not a locking gap - but the sweep only ever revoked STRICTLY EARLIER
/// siblings, so depending purely on which caller happened to win the lock first, the other could
/// still activate on top, leaving either two simultaneously Active cards or two callers both
/// reporting success. <see cref="ActivateReplacementAsync"/> now also refuses to activate any
/// sequence number for which a later, non-Deactivated sibling already exists - the same "only the
/// newest generation may be Active" invariant the earlier-card sweep already enforces, applied in
/// the other direction. This makes the outcome deterministic regardless of scheduling: the
/// lower-sequence call is always the one refused, because the higher-sequence sibling already
/// existed before either call began.
///
/// <b>Tenant context / actor identity.</b> Same disclosed shape as every sibling story: tenant
/// id is read from <see cref="ICurrentTenantProvider"/> only, never a caller-supplied value, and
/// every actor is for audit ATTRIBUTION only (ADR-0006 Decision 1) - nothing about who may
/// report/recover/replace/activate/check is gated on the asserted identity.
/// </summary>
internal sealed class CardLifecycleService(
    ICurrentTenantProvider tenantProvider,
    ICardLifecycleRepository repository,
    ICardIssueGateway cardIssueGateway,
    ICardRecoveryPolicyStore policyStore,
    ICardLifecycleAuditSink auditSink,
    IPresentationEventLog presentationLog,
    CardLifecycleLockRegistry lockRegistry,
    IIdempotencyStore idempotencyStore,
    IProductionStatusGateway productionStatusGateway,
    ICardActivationBarrier activationBarrier,
    TimeSpan? idempotencyWaitTimeout = null,
    TimeSpan? idempotencyWaitPollInterval = null)
{
    private const string ActivateReplacementOperation = "activate_replacement";
    private const string IntakePresentationEventOperation = "intake_presentation_event";
    private const string SupersededReasonCode = "superseded_by_replacement";

    // TD-CRD-006-IDEMPOTENCY-RACE's own remediation: how long a claim-loser (a concurrent caller
    // whose Idempotency-Key is already owned by someone else's in-flight attempt) waits for that
    // owner's result before giving up with a 503 (ADR-0004/BIO-ARC-004's documented "claim-loser
    // waits bounded then idempotency_wait_timeout" contract). Both are injectable (constructor
    // parameters, defaulting to production-sized values) so a test can prove the timeout path
    // deterministically and fast, without actually waiting multiple real seconds.
    private readonly TimeSpan _idempotencyWaitTimeout = idempotencyWaitTimeout ?? TimeSpan.FromSeconds(5);
    private readonly TimeSpan _idempotencyWaitPollInterval = idempotencyWaitPollInterval ?? TimeSpan.FromMilliseconds(20);

    /// <summary>BIO-CRD-006-AC01: reported active card immediately becomes locked/denied with a configurable reason.</summary>
    public async Task<CardLifecycleRecord> ReportAsync(
        string personId, string cardClass, int sequenceNumber, string reasonCode, string? reason, int? expectedVersion,
        AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();

        var errors = new List<Api.ValidationErrorItem>();
        var normalisedReason = ValidateRequiredField(reasonCode, "reason_code", errors);
        ThrowIfInvalid(errors);

        using var _ = await lockRegistry.AcquireAsync(tenantId, personId, cardClass, cancellationToken);

        if (await cardIssueGateway.GetAsync(personId, cardClass, sequenceNumber, cancellationToken) is null)
        {
            throw CardLifecycleError.CardIssueNotFound(personId, cardClass, sequenceNumber);
        }

        var current = repository.Get(tenantId, personId, cardClass, sequenceNumber);
        var now = UtcInstant.Now();

        CardLifecycleRecord candidate;
        if (current is null)
        {
            candidate = new CardLifecycleRecord(tenantId, personId, cardClass, sequenceNumber)
            {
                LockState = CardLockState.Locked,
                Version = 1,
                ReasonCode = normalisedReason!,
                Reason = reason,
                ReportedAt = now,
                ReportedBy = actor,
                CreatedAt = now,
                UpdatedAt = now,
            };
        }
        else
        {
            RequireExpectedVersion(expectedVersion, current.Version);

            if (current.LockState == CardLockState.Locked)
            {
                throw CardLifecycleError.AlreadyReported(personId, cardClass, sequenceNumber);
            }

            if (current.LockState is CardLockState.RevocationPending or CardLockState.PermanentlyRevoked)
            {
                // RevocationPending: an activate-replacement sweep has this card mid-flight -
                // its fate is not yet decided (see CardLockState's own header); PermanentlyRevoked
                // is simply terminal. Either way, report cannot proceed until that resolves.
                throw CardLifecycleError.InvalidTransition("report", current.LockState);
            }

            // else: Recovered or Unlocked - both are legitimate starting points to (re-)report.
            candidate = current with
            {
                LockState = CardLockState.Locked,
                ReasonCode = normalisedReason!,
                Reason = reason,
                ReportedAt = now,
                ReportedBy = actor,
                RecoveredAt = null,
                RecoveredBy = null,
                Version = current.Version + 1,
                UpdatedAt = now,
            };
        }

        await AuditAsync(tenantId, actor, "card_lifecycle.report", personId, cardClass, sequenceNumber, "card_reported", correlationId,
            new Dictionary<string, (object?, string)> { ["reason_code"] = (normalisedReason, "internal") }, cancellationToken);

        return current is null ? repository.Create(candidate) : repository.Replace(current, candidate);
    }

    /// <summary>BIO-CRD-006-AC02: before a replacement activates, permitted recovery may reactivate according to policy.</summary>
    public async Task<CardLifecycleRecord> RecoverAsync(
        string personId, string cardClass, int sequenceNumber, int? expectedVersion,
        AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();

        using var _ = await lockRegistry.AcquireAsync(tenantId, personId, cardClass, cancellationToken);

        var current = repository.Get(tenantId, personId, cardClass, sequenceNumber)
            ?? throw CardLifecycleError.RecordNotFound(personId, cardClass, sequenceNumber);

        RequireExpectedVersion(expectedVersion, current.Version);

        if (current.LockState != CardLockState.Locked)
        {
            // AC04's own sweep already moved a superseded record to PermanentlyRevoked before
            // any later replacement could activate, so "not Locked" here always means either
            // "never reported" (RecordNotFound above already handled that) or "already
            // permanently revoked by a later replacement" - AC02's own "before replacement
            // activation" boundary, enforced structurally rather than by a redundant check.
            throw CardLifecycleError.InvalidTransition("recover", current.LockState);
        }

        if (!policyStore.IsRecoveryPermitted(tenantId, current.ReasonCode))
        {
            throw CardLifecycleError.RecoveryNotPermitted(current.ReasonCode);
        }

        var now = UtcInstant.Now();
        var updated = current with { LockState = CardLockState.Recovered, RecoveredAt = now, RecoveredBy = actor, Version = current.Version + 1, UpdatedAt = now };

        await AuditAsync(tenantId, actor, "card_lifecycle.recover", personId, cardClass, sequenceNumber, "card_recovered", correlationId,
            new Dictionary<string, (object?, string)> { ["reason_code"] = (current.ReasonCode, "internal") }, cancellationToken);

        return repository.Replace(current, updated);
    }

    /// <summary>BIO-CRD-006-AC03: replacement uses next sequence - a pure passthrough, since BIO-CRD-005's own repository already guarantees that.</summary>
    public Task<CardIssueSnapshot> ReplaceAsync(
        string personId, string cardClass, ReplacementCardRequest request,
        AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        RequireTenant();
        return cardIssueGateway.IssueReplacementAsync(personId, cardClass, request, actor, correlationId, cancellationToken);
    }

    /// <summary>
    /// BIO-CRD-006-AC04: permanently revokes every earlier physical card for this (person, card
    /// class) FIRST, and only once that whole sweep has fully committed does it activate the
    /// replacement - see class header ("BIO-CRD-006-QA-01") for why this order, not the reverse,
    /// is what makes AC04 a true invariant of every observable state rather than merely the
    /// eventual end state after a caller retries. See class header ("BIO-CRD-006-QA-02") for why
    /// the replacement's own activation preconditions are reserved and validated BEFORE any of
    /// that revocation, not after.
    /// </summary>
    public async Task<CardIssueSnapshot> ActivateReplacementAsync(
        string personId, string cardClass, int sequenceNumber, int? expectedVersion, string idempotencyKey,
        AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw CardLifecycleError.IdempotencyKeyRequired();
        }

        // BIO-CRD-006-QA-02: reserve the exact request at THIS story's own layer before anything
        // else - a genuine replay short-circuits here, before any side effect; a conflicting
        // reuse is rejected immediately, the same shape IntakePresentationEventAsync already
        // uses.
        //
        // TD-CRD-006-IDEMPOTENCY-RACE: the original code checked only `claimed.State ==
        // Completed` - a shared InFlight record from a CONCURRENT caller looked identical to a
        // claim this caller itself just won, so a second caller fell through and activated a
        // second time, silently re-running the whole revocation sweep. `IdempotencyClaim.IsNewClaim`
        // is the missing ownership signal: only the true owner ever breaks out of this loop to
        // proceed below. Every other caller (someone else's claim is in flight) waits, bounded by
        // `_idempotencyWaitTimeout`, re-attempting its own `Claim` each iteration.
        var fingerprint = $"{personId}\u0000{cardClass}\u0000{sequenceNumber}\u0000{expectedVersion}";
        var deadline = DateTime.UtcNow + _idempotencyWaitTimeout;
        IdempotencyClaim claim;
        while (true)
        {
            claim = idempotencyStore.Claim(tenantId, ActivateReplacementOperation, idempotencyKey, fingerprint);
            if (claim.Record.RequestFingerprint != fingerprint)
            {
                throw CardLifecycleError.IdempotencyKeyReuse();
            }

            if (claim.Record.State == IdempotencyRecordState.Completed)
            {
                return (CardIssueSnapshot)claim.Record.Result!;
            }

            if (claim.IsNewClaim)
            {
                break;
            }

            if (DateTime.UtcNow >= deadline)
            {
                throw CardLifecycleError.IdempotencyWaitTimeout(idempotencyKey);
            }

            await Task.Delay(_idempotencyWaitPollInterval, cancellationToken);
        }

        try
        {
            using var _ = await lockRegistry.AcquireAsync(tenantId, personId, cardClass, cancellationToken);

            // BIO-OPS-003-AC04 round 2 (SY-OPS003-02, Kemi's ruling): close the real
            // time-of-check-to-time-of-use race, not just the sequential gap round 1 closed - the
            // production-status read below and the real activation call further down must be
            // serialised against BIO-CRD-007's own concurrent mutations on the SAME replacement
            // sequence number, not merely against this story's own CardLifecycleLockRegistry.
            // Held from right here through the activation call completing (see
            // ICardActivationBarrier's own header for why this method never needs to hold it for
            // any EARLIER sequence number the sweep below also touches).
            using var __ = await activationBarrier.AcquireAsync(tenantId, personId, cardClass, sequenceNumber, cancellationToken);

            // BIO-OPS-003-AC04 round 1 (Kemi's ruling): production must have Passed BEFORE any
            // irreversible revocation side effect - same reasoning as QA-02 below, just a second
            // precondition. Checked here, right after the lock and before the sweep, so a
            // card whose production never passed never gets its earlier siblings' recoverability
            // destroyed for an activation that was always going to be refused.
            var production = await productionStatusGateway.HasPassedAsync(personId, cardClass, sequenceNumber, cancellationToken);
            if (!production.HasPassed)
            {
                throw CardLifecycleError.ProductionNotPassed(production.LatestJobSequenceNumber, production.LatestStatus);
            }

            // BIO-CRD-006-QA-02: validate the replacement's own activation preconditions BEFORE
            // any irreversible revocation side effect - see class header for why this cannot be
            // a real second call to the gateway's own ActivateAsync.
            var target = await cardIssueGateway.GetAsync(personId, cardClass, sequenceNumber, cancellationToken)
                ?? throw CardLifecycleError.CardIssueNotFound(personId, cardClass, sequenceNumber);

            if (target.Status == CardIssueLifecycleStatus.Deactivated)
            {
                throw CardLifecycleError.ReplacementNotActivatable(sequenceNumber, target.Status.ToString().ToLowerInvariant());
            }

            if (target.Status == CardIssueLifecycleStatus.Issued)
            {
                RequireExpectedVersion(expectedVersion, target.Version);
            }
            // else Active: either this exact Idempotency-Key's own Completed record already
            // short-circuited above in the normal case, or BIO-CRD-005's own raw endpoint was
            // called directly and concurrently (an existing, disclosed composition limitation -
            // see class header). No local precondition to enforce here; the sweep below is a
            // no-op for anything already revoked, and the real gateway call below remains
            // authoritative, including its own safe replay.

            var siblingsBefore = await cardIssueGateway.ListAsync(personId, cardClass, cancellationToken);

            // BIO-OPS-007-AC01 (Femi's concurrent-replacement hardening find): two outstanding,
            // never-yet-activated replacements (e.g. sequence 2 and sequence 3, both Issued while
            // sequence 1 was still Active) raced into this method concurrently. This story's own
            // `lockRegistry` fully serialises the two calls - each runs this whole method's body
            // one after the other, never truly in parallel - so by itself that is NOT the bug:
            // whichever call runs second already observes the first call's committed writes. The
            // actual gap is that the sweep below only ever revokes STRICTLY EARLIER siblings
            // (`SequenceNumber < sequenceNumber`) - nothing here ever checked whether a LATER
            // sibling already exists. Depending on which of the two serialised calls happens to
            // run first, that let either (a) the lower-sequence call run first and successfully
            // activate, only to be correctly superseded later when the higher-sequence call's own
            // sweep caught it as an "earlier" card - leaving the right end state but BOTH calls
            // reporting success, or (b) the higher-sequence call run first and activate, after
            // which the lower-sequence call's sweep never looks at sequence numbers ABOVE its own
            // and activates anyway - leaving TWO simultaneously Active cards, a real, observable
            // violation of "at most one Active card per person/card class" regardless of ordering.
            // The fix is symmetric to the earlier-card sweep: a replacement may only ever activate
            // if it is the LATEST live (non-Deactivated) sequence number for this person/card
            // class - the same "only the newest generation may become Active" rule the earlier
            // sweep already enforces in the other direction. This makes the outcome fully
            // deterministic regardless of which concurrent caller wins the lock race: the
            // lower-sequence call is always refused, because the higher-sequence sibling already
            // existed (as an Issued record) before either call began - never because of who ran
            // first.
            var laterLiveCards = siblingsBefore
                .Where(s => s.SequenceNumber > sequenceNumber && s.Status != CardIssueLifecycleStatus.Deactivated)
                .ToList();
            if (laterLiveCards.Count > 0)
            {
                throw CardLifecycleError.SupersededByLaterReplacement(sequenceNumber, laterLiveCards.Max(s => s.SequenceNumber));
            }

            var earlierCards = siblingsBefore.Where(s => s.SequenceNumber < sequenceNumber).ToList();

            // BIO-CRD-005's own ActivateAsync refuses a second Active card outright (its own
            // AC04) - whichever earlier card is still Active there must be cleared first, or the
            // replacement could never activate at all. Best-effort/idempotent: a retry after a
            // partial failure may find this already Deactivated, which is a no-op, not an error -
            // the sweep below is this story's own authoritative record of permanent revocation
            // regardless of whether this best-effort secondary sync of BIO-CRD-005's own record
            // succeeds.
            foreach (var stillActive in earlierCards.Where(s => s.Status == CardIssueLifecycleStatus.Active))
            {
                try
                {
                    await cardIssueGateway.DeactivateAsync(stillActive.PersonId, stillActive.CardClass, stillActive.SequenceNumber, stillActive.Version, actor, correlationId, cancellationToken);
                }
                catch
                {
                    // Best-effort - see the sweep below for why this is safe to ignore.
                }
            }

            // BIO-CRD-006-QA-01/QA-03: every earlier card must already be at least
            // RevocationPending - denied, non-recoverable - BEFORE the replacement is allowed to
            // actually activate; see class header. If any one of these throws (e.g. this story's
            // own audit sink is briefly unavailable), the replacement is never activated at all
            // this attempt; the caller may safely retry with the same Idempotency-Key, which
            // resumes exactly where the sweep left off.
            var pending = new List<CardLifecycleRecord>();
            foreach (var earlier in earlierCards)
            {
                var beginResult = await BeginRevocationAsync(tenantId, earlier, actor, correlationId, cancellationToken);
                if (beginResult is not null)
                {
                    pending.Add(beginResult);
                }
            }

            CardIssueSnapshot activated;
            try
            {
                activated = await cardIssueGateway.ActivateAsync(personId, cardClass, sequenceNumber, expectedVersion, idempotencyKey, actor, correlationId, cancellationToken);
            }
            catch
            {
                // BIO-CRD-006-QA-03: the authoritative activation failed AFTER the sweep already
                // moved these records to RevocationPending (e.g. a concurrent mutation raced
                // BIO-CRD-005's own compare-and-set) - compensate every one of them back to its
                // recorded prior state rather than leaving them destroyed for an activation that
                // never actually happened.
                foreach (var pendingRecord in pending)
                {
                    await CompensateRevocationAsync(tenantId, pendingRecord, actor, correlationId, cancellationToken);
                }

                throw;
            }

            // The activation succeeded - every pending record's fate is now decided. A failure
            // here (e.g. the audit sink is unavailable for the confirmation write) must NOT
            // compensate - the replacement is genuinely, externally Active now, and undoing the
            // revocation would incorrectly restore recoverability to cards a real replacement has
            // legitimately superseded. It leaves affected records at RevocationPending - still
            // denied, still non-recoverable, so QA-01's own invariant still holds - until a retry
            // (of this same idempotency-reserved request) resumes and completes confirmation.
            foreach (var pendingRecord in pending)
            {
                await ConfirmRevocationAsync(tenantId, pendingRecord, actor, correlationId, cancellationToken);
            }

            idempotencyStore.Complete(tenantId, ActivateReplacementOperation, idempotencyKey, activated);
            return activated;
        }
        catch
        {
            idempotencyStore.Release(tenantId, ActivateReplacementOperation, idempotencyKey);
            throw;
        }
    }

    /// <summary>Locked/Recovered/no-record → RevocationPending, recording the prior state to compensate back to. Idempotent: already-PermanentlyRevoked returns null (nothing left to do this attempt); already-RevocationPending (a prior, interrupted attempt) is returned as-is, its own durably-recorded prior state untouched.</summary>
    private async Task<CardLifecycleRecord?> BeginRevocationAsync(string tenantId, CardIssueSnapshot earlier, AuditActor actor, string correlationId, CancellationToken cancellationToken)
    {
        var current = repository.Get(tenantId, earlier.PersonId, earlier.CardClass, earlier.SequenceNumber);
        if (current is not null && current.LockState == CardLockState.PermanentlyRevoked)
        {
            return null; // already fully confirmed by an earlier attempt of this same idempotent sweep
        }

        if (current is not null && current.LockState == CardLockState.RevocationPending)
        {
            return current; // already begun by an earlier, interrupted attempt - reuse as-is
        }

        var now = UtcInstant.Now();
        CardLifecycleRecord candidate = current is null
            ? new CardLifecycleRecord(tenantId, earlier.PersonId, earlier.CardClass, earlier.SequenceNumber)
            {
                LockState = CardLockState.RevocationPending,
                PriorLockStateBeforeRevocationPending = null,
                Version = 1,
                ReasonCode = SupersededReasonCode,
                ReportedAt = now,
                ReportedBy = actor,
                CreatedAt = now,
                UpdatedAt = now,
            }
            : current with
            {
                LockState = CardLockState.RevocationPending,
                PriorLockStateBeforeRevocationPending = current.LockState,
                Version = current.Version + 1,
                UpdatedAt = now,
            };

        await AuditAsync(tenantId, actor, "card_lifecycle.begin_revoke", earlier.PersonId, earlier.CardClass, earlier.SequenceNumber, "card_revocation_pending", correlationId,
            new Dictionary<string, (object?, string)> { ["reason_code"] = (SupersededReasonCode, "internal") }, cancellationToken);

        return current is null ? repository.Create(candidate) : repository.Replace(current, candidate);
    }

    /// <summary>RevocationPending → PermanentlyRevoked, once the replacement's own activation is confirmed to have actually succeeded.</summary>
    private async Task ConfirmRevocationAsync(string tenantId, CardLifecycleRecord pendingRecord, AuditActor actor, string correlationId, CancellationToken cancellationToken)
    {
        var current = repository.Get(tenantId, pendingRecord.PersonId, pendingRecord.CardClass, pendingRecord.SequenceNumber);
        if (current is not null && current.LockState == CardLockState.PermanentlyRevoked)
        {
            return; // already confirmed by an earlier attempt
        }

        var expected = current ?? pendingRecord;
        var now = UtcInstant.Now();
        var confirmed = expected with
        {
            LockState = CardLockState.PermanentlyRevoked,
            PriorLockStateBeforeRevocationPending = null,
            RevokedAt = now,
            RevokedBy = actor,
            RevokedReasonCode = SupersededReasonCode,
            Version = expected.Version + 1,
            UpdatedAt = now,
        };

        await AuditAsync(tenantId, actor, "card_lifecycle.confirm_revoke", pendingRecord.PersonId, pendingRecord.CardClass, pendingRecord.SequenceNumber, "card_permanently_revoked", correlationId,
            new Dictionary<string, (object?, string)> { ["reason_code"] = (SupersededReasonCode, "internal") }, cancellationToken);

        // The one earlier card that was Active has already been deactivated in BIO-CRD-005's own
        // record (best-effort) by ActivateReplacementAsync, before the sweep and before the
        // replacement's own activation - see that method's own body. This story's own
        // CardLockState.PermanentlyRevoked, just committed here, remains the sole and
        // authoritative denial gate regardless of whether that best-effort secondary sync
        // actually succeeded.
        repository.Replace(expected, confirmed);
    }

    /// <summary>RevocationPending → its recorded prior state (or Unlocked if it had none), because the replacement's own authoritative activation ultimately failed - BIO-CRD-006-QA-03's own compensating transition.</summary>
    private async Task CompensateRevocationAsync(string tenantId, CardLifecycleRecord pendingRecord, AuditActor actor, string correlationId, CancellationToken cancellationToken)
    {
        var current = repository.Get(tenantId, pendingRecord.PersonId, pendingRecord.CardClass, pendingRecord.SequenceNumber);
        if (current is null || current.LockState != CardLockState.RevocationPending)
        {
            return; // already resolved (confirmed or compensated) by another path - nothing to undo
        }

        var revertedState = current.PriorLockStateBeforeRevocationPending ?? CardLockState.Unlocked;
        var now = UtcInstant.Now();
        var reverted = current with
        {
            LockState = revertedState,
            PriorLockStateBeforeRevocationPending = null,
            Version = current.Version + 1,
            UpdatedAt = now,
        };

        await AuditAsync(tenantId, actor, "card_lifecycle.compensate_revoke", pendingRecord.PersonId, pendingRecord.CardClass, pendingRecord.SequenceNumber, "card_revocation_compensated", correlationId,
            new Dictionary<string, (object?, string)> { ["reverted_to"] = (revertedState.ToWireString(), "internal") }, cancellationToken);

        repository.Replace(current, reverted);
    }

    /// <summary>BIO-CRD-006-AC05 (online path): a connected reader's live allow/deny decision, logged the same as an offline one.</summary>
    public async Task<PresentationCheckResult> CheckPresentationAsync(
        string personId, string cardClass, int sequenceNumber, string? deviceRef,
        AuditActor actor, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();

        var record = repository.Get(tenantId, personId, cardClass, sequenceNumber);
        var (outcome, reasonCode) = Evaluate(record);

        var now = UtcInstant.Now();
        var evt = new PresentationEvent
        {
            EventId = CanonicalId.NewEventId(),
            TenantId = tenantId,
            PersonId = personId,
            CardClass = cardClass,
            SequenceNumber = sequenceNumber,
            Outcome = outcome,
            ReasonCode = reasonCode,
            DeviceRef = deviceRef,
            RecordedBy = actor,
            Source = "online_check",
            OccurredAt = now,
            ReceivedAt = now,
        };

        await AppendPresentationEventAsync(evt, cancellationToken);
        return new PresentationCheckResult(outcome, reasonCode, evt.EventId);
    }

    /// <summary>
    /// BIO-CRD-006-AC05 (offline path): ADR-0004 §4.3's own "offline sync intake" - an offline
    /// reader already decided ALLOW/DENY locally from its cached deny list
    /// (<see cref="GetDenyListAsync"/>) while it had no connectivity, and submits that decision
    /// here once connectivity resumes. This intentionally does NOT re-derive or override the
    /// reader's own decision against this server's current state - the entire point of offline
    /// operation is that the reader decided without asking this server, and a stale local deny
    /// list is exactly what a genuinely revoked card is supposed to still be denied against.
    /// </summary>
    public async Task<PresentationEvent> IntakePresentationEventAsync(
        string personId, string cardClass, int sequenceNumber, PresentationOutcome outcome, string reasonCode, string occurredAt,
        string? deviceRef, string idempotencyKey, AuditActor actor, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw CardLifecycleError.IdempotencyKeyRequired();
        }

        var errors = new List<Api.ValidationErrorItem>();
        var normalisedReason = ValidateRequiredField(reasonCode, "reason_code", errors);
        var normalisedOccurredAt = ValidateRequiredField(occurredAt, "occurred_at", errors);
        ThrowIfInvalid(errors);

        // TD-CRD-006-IDEMPOTENCY-RACE: see ActivateReplacementAsync's own header for why checking
        // `claimed.State == Completed` alone is the bug - the same fix shape applies here.
        var fingerprint = $"{personId}\u0000{cardClass}\u0000{sequenceNumber}\u0000{outcome}\u0000{normalisedReason}\u0000{normalisedOccurredAt}\u0000{deviceRef}\u0000{actor.Type}\u0000{actor.Id}";
        var deadline = DateTime.UtcNow + _idempotencyWaitTimeout;
        while (true)
        {
            var claim = idempotencyStore.Claim(tenantId, IntakePresentationEventOperation, idempotencyKey, fingerprint);
            if (claim.Record.RequestFingerprint != fingerprint)
            {
                throw CardLifecycleError.IdempotencyKeyReuse();
            }

            if (claim.Record.State == IdempotencyRecordState.Completed)
            {
                return (PresentationEvent)claim.Record.Result!;
            }

            if (claim.IsNewClaim)
            {
                break;
            }

            if (DateTime.UtcNow >= deadline)
            {
                throw CardLifecycleError.IdempotencyWaitTimeout(idempotencyKey);
            }

            await Task.Delay(_idempotencyWaitPollInterval, cancellationToken);
        }

        try
        {
            var evt = new PresentationEvent
            {
                EventId = CanonicalId.NewEventId(),
                TenantId = tenantId,
                PersonId = personId,
                CardClass = cardClass,
                SequenceNumber = sequenceNumber,
                Outcome = outcome,
                ReasonCode = normalisedReason!,
                DeviceRef = deviceRef,
                RecordedBy = actor,
                Source = "offline_sync",
                OccurredAt = normalisedOccurredAt!,
                ReceivedAt = UtcInstant.Now(),
            };

            await AppendPresentationEventAsync(evt, cancellationToken);
            idempotencyStore.Complete(tenantId, IntakePresentationEventOperation, idempotencyKey, evt);
            return evt;
        }
        catch
        {
            idempotencyStore.Release(tenantId, IntakePresentationEventOperation, idempotencyKey);
            throw;
        }
    }

    /// <summary>AC05's local deny list an offline reader pulls before going offline. Both <see cref="CardLockState.Locked"/> and <see cref="CardLockState.PermanentlyRevoked"/> deny.</summary>
    public Task<IReadOnlyList<CardLifecycleRecord>> GetDenyListAsync(string? since, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        return Task.FromResult(repository.ListDenyList(tenantId, since));
    }

    public Task<CardLifecycleRecord?> GetRecordAsync(string personId, string cardClass, int sequenceNumber, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        return Task.FromResult(repository.Get(tenantId, personId, cardClass, sequenceNumber));
    }

    public void SetRecoveryPolicy(string reasonCode, bool recoveryPermitted)
    {
        var tenantId = RequireTenant();
        var normalised = reasonCode?.Trim();
        if (string.IsNullOrEmpty(normalised))
        {
            throw CardLifecycleError.Validation([new Api.ValidationErrorItem("reason_code", "required", "reason_code is required")]);
        }

        policyStore.SetPolicy(tenantId, normalised, recoveryPermitted);
    }

    private static (PresentationOutcome Outcome, string ReasonCode) Evaluate(CardLifecycleRecord? record) => record?.LockState switch
    {
        CardLockState.PermanentlyRevoked => (PresentationOutcome.Denied, record.RevokedReasonCode ?? SupersededReasonCode),
        CardLockState.Locked => (PresentationOutcome.Denied, record!.ReasonCode),
        // BIO-CRD-006-QA-03: a card mid-sweep, not yet confirmed, is denied conservatively - its
        // fate is not yet decided, and QA-01's own invariant depends on it never reading Allowed.
        CardLockState.RevocationPending => (PresentationOutcome.Denied, "revocation_pending"),
        // Recovered, Unlocked, or no record at all - each behaves as "not currently locked."
        _ => (PresentationOutcome.Allowed, "card_not_revoked"),
    };

    private async Task AppendPresentationEventAsync(PresentationEvent evt, CancellationToken cancellationToken)
    {
        try
        {
            await presentationLog.AppendAsync(evt, cancellationToken);
            return;
        }
        catch (Exception)
        {
            // Ambiguous - fall through to verify rather than assume failure.
        }

        bool exists;
        try
        {
            exists = await presentationLog.ExistsAsync(evt.EventId, cancellationToken);
        }
        catch (Exception)
        {
            throw CardLifecycleError.AuditUnavailable();
        }

        if (!exists)
        {
            throw CardLifecycleError.AuditUnavailable();
        }
    }

    private async Task AuditAsync(
        string tenantId, AuditActor actor, string action, string personId, string cardClass, int sequenceNumber, string reasonCode,
        string correlationId, IReadOnlyDictionary<string, (object? Value, string Classification)> detail,
        CancellationToken cancellationToken)
    {
        var auditEvent = CardLifecycleAuditEventBuilder.Build(
            tenantId, actor, action, new AuditTarget("card_issue", $"{personId}:{cardClass}:{sequenceNumber}"),
            AuditOutcome.Allowed, reasonCode, correlationId, causationId: null, detail);

        try
        {
            await auditSink.WriteAsync(auditEvent, cancellationToken);
            return;
        }
        catch (Exception)
        {
            // Ambiguous - fall through to verify rather than assume failure.
        }

        bool exists;
        try
        {
            exists = await auditSink.ExistsAsync(auditEvent.EventId, cancellationToken);
        }
        catch (Exception)
        {
            throw CardLifecycleError.AuditUnavailable();
        }

        if (!exists)
        {
            throw CardLifecycleError.AuditUnavailable();
        }
    }

    private static void RequireExpectedVersion(int? expectedVersion, int actualVersion)
    {
        if (expectedVersion is not int expected)
        {
            throw CardLifecycleError.PreconditionRequired();
        }

        if (expected != actualVersion)
        {
            throw CardLifecycleError.ConcurrencyConflict(expected, actualVersion);
        }
    }

    private string RequireTenant() => tenantProvider.TenantId ?? throw CardLifecycleError.TenantContextMissing();

    private static string? ValidateRequiredField(string? value, string field, List<Api.ValidationErrorItem> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(new Api.ValidationErrorItem(field, "required", $"{field} is required"));
            return null;
        }

        return value.Trim();
    }

    private static void ThrowIfInvalid(List<Api.ValidationErrorItem> errors)
    {
        if (errors.Count > 0)
        {
            throw CardLifecycleError.Validation(errors);
        }
    }
}

public sealed record PresentationCheckResult(PresentationOutcome Outcome, string ReasonCode, string EventId);
