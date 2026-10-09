using Biolog.Features.VisitorCredentials.Api;
using Biolog.Features.VisitorCredentials.Audit;
using Biolog.Features.VisitorCredentials.Domain;
using Biolog.Features.VisitorCredentials.Identifiers;
using Biolog.Features.VisitorCredentials.Tenancy;

namespace Biolog.Features.VisitorCredentials;

/// <summary>
/// Application service for AC02 (grant - expiry+scope, backed by a real BIO-CRD-005 card), AC03
/// (revoke/checkout - ends the backing card too) and AC05 (history). Implements ADR-0003
/// Decision 7's audit-atomicity pattern exactly as established across this codebase: a
/// per-<c>(TenantId, VisitorRecordId)</c> lock (<see cref="VisitorCredentialLockRegistry"/>),
/// audit-before-commit, <see cref="IVisitorCredentialAuditSink.ExistsAsync"/> verification on an
/// ambiguous audit-write throw, <see cref="CancellationToken.None"/> on the repository commit
/// once the audit write is confirmed durable.
///
/// <b><see cref="GrantAsync"/>/<see cref="RevokeAsync"/> are this feature's authorization
/// decisions (ADR-0006 Decision 1/2)</b> - granting or revoking physical access requires
/// <see cref="ManagePermission"/> via <see cref="IPermissionGateway"/> AND a verified ADR-0006
/// Decision 3 provenance proof (<see cref="ActorProvenanceGate"/>). <see cref="CheckOutAsync"/>
/// stays open to any asserted actor - audit-attribution only, mirroring BIO-VIS-001's own
/// <c>CheckOutAsync</c>, which this story's own orchestrator calls through to via
/// <see cref="IVisitorRecordGateway"/> before revoking any active credential as a direct,
/// automatic CONSEQUENCE of checking out (AC03's own wording), not a second human decision -
/// revocation triggered this way never requires the grant/revoke permission.
///
/// <b>Post-review remediation (Seyi's FAIL on commit 45f4736, evidence/BIO-VIS-002/codex/REVIEW-Seyi.md).</b>
///
/// <i>F01 - grant failure strands an activated card.</i> The original <c>GrantAsync</c> called
/// BIO-CRD-005 <c>Issue</c>+<c>Activate</c> BEFORE writing anything local, so a failure in the
/// final local write left a genuinely Active card with no local row to find/revoke it, and a
/// retry's duplicate check (consulting only that missing local row) would blindly issue a SECOND
/// card. Fixed by: (1) persisting a <see cref="VisitorCredentialStatus.Pending"/> anchor FIRST,
/// under the same per-visitor lock held for the whole operation; (2) checking BIO-CRD-005's OWN
/// state for this (person, card class) via <see cref="ICardIssuanceGateway.ListAsync"/> before
/// calling <c>IssueAsync</c> - adopting an already-issued/active card from a prior partial
/// failure instead of minting a new one (the concrete fix for "retries must not issue
/// duplicates"); (3) on a confirmed final-commit failure (verified via the same
/// <c>ExistsAsync</c>-on-throw trick this codebase already uses for an ambiguous audit-write),
/// a best-effort compensating <c>DeactivateAsync</c> plus a terminal
/// <see cref="VisitorCredentialStatus.Failed"/> local record carrying the card's identifiers -
/// never left dangling <see cref="VisitorCredentialStatus.Pending"/>. <b>Disclosed residual
/// risk:</b> true two-phase-commit across two independent in-memory stores is out of scope; if
/// BOTH the compensating deactivate AND the Failed-mark write themselves fail (a double failure),
/// the local anchor can be left stuck Pending with a genuinely Active, un-recorded card - the
/// NEXT grant attempt's own pre-issue <c>ListAsync</c> check (point 2) still finds and adopts
/// that card rather than minting a third one, which is the concrete guarantee Seyi asked for
/// ("retries must not issue duplicates"), even though full local bookkeeping may not converge in
/// this specific double-failure case. See <c>docs/features/bio-vis-002/README.md</c> "Post-review
/// remediation" for the full record.
///
/// <i>F02 - checkout failure can leave active access after the visit ends.</i> The original
/// <c>CheckOutAsync</c> committed BIO-VIS-001's own checkout FIRST, then revoked the credential -
/// a failure in the revoke step left a visitor checked-out with still-active physical access, and
/// BIO-VIS-001's own checkout being one-way meant no retry could ever complete it. Fixed by
/// REORDERING, not merely making the old order "recoverable" (a deliberately stronger fix than
/// Seyi's own stated minimum): revoke now happens FIRST, while the visitor is still checked-in (a
/// safe, retryable state), and BIO-VIS-001's own checkout is the final, point-of-no-return step -
/// a visitor is never checked-out while still holding active access, not even transiently. The
/// whole orchestrator is idempotent on retry: if the visitor is already checked out (via a prior
/// run of this same orchestrator, or via BIO-VIS-001's own direct endpoint with no credential ever
/// granted), it is a no-op success, never a second call into BIO-VIS-001's one-way checkout.
///
/// <i>F05 - a ledgered stranded card remained active indefinitely.</i> F04's own
/// <see cref="IStrandedCardLedger"/> only ever stopped a stranded card being ADOPTED by a future
/// grant - it did nothing about the card's own genuinely-<c>Active</c> state, so a failed,
/// unaudited grant could leave real, usable physical access indefinitely (both Seyi and Zainab
/// independently flagged this on the very test F04 added to prove its own fix). Fixed by
/// <see cref="ReconcileStrandedCardsAsync"/> - see its own header for the full design and why
/// reconciliation was chosen over reaching into another story's access-decision layer.
///
/// <i>F06 - two distinct High findings on the F05 fix, one from each reviewer.</i> (1) Zainab:
/// <see cref="GrantAsync"/> swallowed a failed reconciliation attempt and proceeded to issue a
/// BRAND NEW card anyway, so one person could end up holding two simultaneously Active cards (the
/// old stranded one plus the new one). Fixed: if ANY ledger entry for this (person, card class)
/// is still unresolved after the opportunistic reconciliation attempt, <see cref="GrantAsync"/>
/// now fails closed (<see cref="VisitorCredentialError.StrandedCardUnresolved"/>) rather than
/// issuing another card. (2) Seyi: opportunistic reconciliation (only ever triggered by a LATER
/// grant for the same person) is not a guaranteed recovery path - a person who never requests
/// another grant could keep a stranded card active forever. Fixed by adding
/// <see cref="ReconcileStrandedCardsForPersonAsync"/>, a directly callable, permission-gated
/// operator-recovery entry point (mapped as its own real HTTP endpoint,
/// <c>POST /api/v1/visitor-credentials/reconcile</c>) - the "operator-visible recovery command/
/// endpoint" Seyi's own review named as an acceptable guaranteed trigger - plus widening the
/// opportunistic trigger into <see cref="CheckOutAsync"/>/<see cref="RevokeAsync"/> too (a bonus
/// that increases real-world coverage, not the primary fix - the endpoint is).
///
/// <i>F07 - the F06 fix's own reconciliation could resolve a ledger entry without ever durably
/// auditing the deactivation, both reviewers independently.</i> <see cref="ReconcileStrandedCardsAsync"/>
/// deactivated, then audited, then resolved; if the audit write failed after a successful
/// deactivation, a RETRY saw the card already inactive and resolved the ledger immediately,
/// skipping the audit write entirely - the physical-access mutation vanished from history with no
/// trace. Fixed with a stable, per-entry audit event id
/// (<see cref="IStrandedCardLedger.GetOrCreateReconciliationEventIdAsync"/>) so the audit write
/// always runs before resolving, is deterministically retryable, and is never duplicated once a
/// prior attempt's write already durably succeeded. See <see cref="ReconcileStrandedCardsAsync"/>'s
/// own header for the full record.
/// </summary>
internal sealed class VisitorCredentialService(
    ICurrentTenantProvider tenantProvider,
    IVisitorCredentialRepository repository,
    IVisitorRecordGateway visitorGateway,
    ICardIssuanceGateway cardGateway,
    IVisitorCredentialAuditSink auditSink,
    VisitorCredentialLockRegistry lockRegistry,
    IPermissionGateway permissionGateway,
    IStrandedCardLedger strandedCardLedger)
{
    public const string ManagePermission = "visitor.credential_manage";
    private const string CardClass = "visitor_temporary";
    private const string TemplateRef = "visitor-badge-template";
    private const string PrinterRef = "visitor-desk-printer";

    /// <summary>
    /// AC02 - grants a new temporary credential, backed by a real, Active BIO-CRD-005 card.
    ///
    /// <b>Disclosed precondition: the visitor must have a linked PERSON.</b> BIO-VIS-001
    /// deliberately supports a "lead" visitor with a null <c>PersonId</c>; BIO-CRD-005's own
    /// <c>IssueAsync</c> requires a real, existing one. Rather than silently create a credential
    /// with no backing card, or relax BIO-CRD-005's own requirement, this is a hard precondition
    /// - see <see cref="VisitorCredentialError.VisitorHasNoLinkedPerson"/> and
    /// <c>docs/features/bio-vis-002/README.md</c> "Why a credential requires a linked PERSON".
    /// </summary>
    public async Task<VisitorCredential> GrantAsync(
        string visitorId, GrantVisitorCredentialRequest request, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();

        if (!await permissionGateway.ActorHasPermissionAsync(tenantId, actor.Id, ManagePermission, cancellationToken))
        {
            throw VisitorCredentialError.PermissionRequired(ManagePermission);
        }

        // BIO-VIS-002-F03 (Seyi's round-2 FAIL): an unlocked pre-read, used ONLY to resolve the
        // lock key. Two visitor records for the SAME person must serialize their grant attempts
        // against each other - two DIFFERENT visitors' own per-visitor locks never contend, which
        // is exactly how the original adoption bug could race concurrently. Falls back to the
        // visitor id itself only for a null-PersonId "lead" (GrantAsync rejects that case below
        // anyway, so no cross-visitor collision is possible there). Re-read under the lock
        // immediately after for freshness - this pre-read could already be stale.
        var preRead = await visitorGateway.GetAsync(visitorId, cancellationToken) ?? throw VisitorCredentialError.VisitorNotFound(visitorId);
        var lockKey = preRead.PersonId ?? visitorId;

        using var _ = await lockRegistry.AcquireAsync(tenantId, lockKey, cancellationToken);

        var visitor = await visitorGateway.GetAsync(visitorId, cancellationToken) ?? throw VisitorCredentialError.VisitorNotFound(visitorId);

        if (visitor.PersonId is null)
        {
            throw VisitorCredentialError.VisitorHasNoLinkedPerson(visitorId);
        }

        // F05 fix (Seyi round-4 FAIL / Zainab round-3 FAIL): opportunistic reconciliation for this
        // person's own stranded cards, under the same person-scoped lock already held above - see
        // ReconcileStrandedCardsAsync's own header for the full record. Best-effort only: a
        // reconciliation hiccup must never block or fail an otherwise-valid grant attempt that has
        // nothing to do with it.
        try
        {
            await ReconcileStrandedCardsAsync(tenantId, visitor.PersonId, CardClass, actor, correlationId);
        }
        catch
        {
            // Still unresolved (or the ledger itself hiccupped) - the check immediately below
            // is what actually decides whether this grant may proceed, not this catch.
        }

        // F06 fix (Zainab's round-4 FAIL): if reconciliation just above did not fully resolve
        // every stranded entry for this (person, card class), a prior attempt's card may still be
        // genuinely Active and unaccounted for - fail closed rather than falling through to issue
        // a SECOND card, which would leave this person holding two simultaneously Active physical-
        // access credentials. The caller can retry once reconciliation succeeds (another grant, a
        // checkout/revoke for this person, or the dedicated reconcile endpoint).
        var stillUnresolved = await strandedCardLedger.ListUnresolvedAsync(tenantId, visitor.PersonId, CardClass, cancellationToken);
        if (stillUnresolved.Count > 0)
        {
            throw VisitorCredentialError.StrandedCardUnresolved(visitor.PersonId, CardClass, stillUnresolved);
        }

        if (visitor.VisitStatusWire != "checked-in")
        {
            throw VisitorCredentialError.GrantBlocked(visitorId, "not_checked_in", $"visitor must be checked in, not '{visitor.VisitStatusWire}'");
        }

        if (await repository.GetActiveForVisitorAsync(tenantId, visitorId, cancellationToken) is not null)
        {
            throw VisitorCredentialError.GrantBlocked(visitorId, "credential_already_granted", "this visitor already has an active temporary credential");
        }

        var errors = new List<Api.ValidationErrorItem>();
        var scope = VisitorCredentialScope.General;
        if (!string.IsNullOrWhiteSpace(request.Scope) && !VisitorCredentialScopeExtensions.TryParse(request.Scope, out scope))
        {
            errors.Add(new Api.ValidationErrorItem("scope", "invalid", "scope must be one of general, escorted, restricted_area"));
        }

        if (string.IsNullOrWhiteSpace(request.ExpiresAt))
        {
            errors.Add(new Api.ValidationErrorItem("expires_at", "required", "expires_at is required"));
        }
        else if (!UtcInstant.TryParse(request.ExpiresAt, out var parsedExpiresAt))
        {
            errors.Add(new Api.ValidationErrorItem("expires_at", "invalid_format", "expires_at must be an RFC 3339 UTC instant"));
        }

        if (errors.Count > 0)
        {
            throw VisitorCredentialError.Validation(errors);
        }

        var expiresAt = request.ExpiresAt!;
        if (string.CompareOrdinal(expiresAt, visitor.ExpiresAt) > 0) // RFC 3339 UTC sorts lexicographically == chronologically
        {
            throw VisitorCredentialError.ExpiresAtExceedsVisit(visitor.ExpiresAt);
        }

        var now = UtcInstant.Now();
        if (string.CompareOrdinal(expiresAt, now) <= 0)
        {
            throw VisitorCredentialError.Validation([new Api.ValidationErrorItem("expires_at", "out_of_range", "expires_at must be in the future")]);
        }

        var actorTypeWire = actor.Type.ToWireString();

        // F01 fix, step 1: a durable anchor, persisted BEFORE any BIO-CRD-005 call, still under
        // the per-visitor lock acquired above (held for this whole method) - see this class's own
        // header "Post-review remediation".
        var pending = await repository.CreateAsync(
            VisitorCredential.CreatePending(tenantId, visitorId, visitor.PersonId, scope, expiresAt, CardClass, actor.Id, now),
            CancellationToken.None);

        int? issuedSequenceNumber = null;
        try
        {
            // F01 fix, step 2 (and F03/F04's own remediation on top of it): check BIO-CRD-005's OWN
            // state first - adopt an already-issued/active card for this (person, card class) from
            // a prior partial failure instead of blindly issuing a second one. F03 (Seyi's round-2
            // FAIL): a candidate is only adoptable if it is genuinely unowned, or owned by a
            // Pending/Failed/Granted row belonging to THIS SAME visitor record - a candidate
            // already claimed by a DIFFERENT visitor's own credential (a second checked-in visit
            // for the same person, entirely legitimate) must never be adopted, or revoking either
            // visit's credential would silently deactivate the other's card too. F04 (Seyi's
            // round-3 FAIL): a candidate recorded in the stranded-card ledger is excluded from
            // adoption outright, regardless of what FindByCardAsync says - see
            // IStrandedCardLedger's own header.
            var existingCards = await cardGateway.ListAsync(visitor.PersonId, CardClass, cancellationToken);
            CardIssueView? adoptable = null;
            foreach (var candidate in existingCards.Where(c => c.StatusWire is "issued" or "active"))
            {
                if (await strandedCardLedger.IsStrandedAsync(tenantId, visitor.PersonId, CardClass, candidate.SequenceNumber, cancellationToken))
                {
                    continue;
                }

                var owner = await repository.FindByCardAsync(tenantId, visitor.PersonId, CardClass, candidate.SequenceNumber, cancellationToken);
                if (owner is null || owner.VisitorRecordId == visitorId)
                {
                    adoptable = candidate;
                    break;
                }
            }

            CardIssueView card;
            if (adoptable is { StatusWire: "active" } activeCard)
            {
                card = activeCard;
                issuedSequenceNumber = card.SequenceNumber;
            }
            else if (adoptable is { StatusWire: "issued" } issuedCard)
            {
                issuedSequenceNumber = issuedCard.SequenceNumber; // a card already exists - even if activation below throws, there is something to compensate
                var activationKey = $"visitor-credential-activate-{visitor.PersonId}-{CardClass}-{issuedCard.SequenceNumber}";
                card = await cardGateway.ActivateAsync(
                    visitor.PersonId, CardClass, issuedCard.SequenceNumber, issuedCard.Version, activationKey, actor.Id, actorTypeWire, correlationId, cancellationToken);
            }
            else
            {
                var issued = await cardGateway.IssueAsync(visitor.PersonId, CardClass, TemplateRef, PrinterRef, actor.Id, actorTypeWire, correlationId, cancellationToken);
                issuedSequenceNumber = issued.SequenceNumber; // a card now genuinely exists, even if activation below throws
                var activationKey = $"visitor-credential-activate-{visitor.PersonId}-{CardClass}-{issued.SequenceNumber}";
                card = await cardGateway.ActivateAsync(
                    visitor.PersonId, CardClass, issued.SequenceNumber, issued.Version, activationKey, actor.Id, actorTypeWire, correlationId, cancellationToken);
            }

            issuedSequenceNumber = card.SequenceNumber; // confirmed Active from here on

            // F04 fix (Seyi's round-3 FAIL): durably correlate this Pending row to the card's
            // identity NOW, once it is genuinely Active - BEFORE the audit write and BEFORE the
            // final Granted-or-Failed transition - so FindByCardAsync can attribute this exact
            // card even if everything after this point throws. See AttachCardSequenceAsync's own
            // header for the full record and why this single call covers all three paths above
            // (adopt-active, adopt-issued-then-activate, issue-then-activate) uniformly.
            pending = await AttachCardSequenceAsync(pending, card.SequenceNumber);

            await AuditAsync(tenantId, actor, "visitor_credential.granted", pending.Id, "visitor_credential_granted", correlationId,
                new Dictionary<string, (object?, string)>
                {
                    ["visitor_record_id"] = (visitorId, "internal"),
                    ["scope"] = (scope.ToWireString(), "internal"),
                    ["card_sequence_number"] = (card.SequenceNumber, "internal"),
                },
                cancellationToken);

            var granted = pending.MarkGranted(card.SequenceNumber, now);
            return await repository.ReplaceAsync(pending, granted, CancellationToken.None);
        }
        catch (Exception)
        {
            if (issuedSequenceNumber is int seq)
            {
                // The card may genuinely be Active. Verify whether the final commit actually
                // landed despite the throw (the same ambiguous-failure verification this codebase
                // already uses for an audit-write) before deciding to compensate.
                //
                // BIO-VIS-002-F03 (Zainab's round-2 FAIL): CancellationToken.None, deliberately,
                // not the caller's own token - once a card may genuinely be Active, a cancellation
                // of the ORIGINAL request must never be able to suppress this recovery path. The
                // in-memory repository/card gateway both call ThrowIfCancellationRequested(); if
                // this reread (or the compensation below) reused the caller's token and the caller
                // had just cancelled, compensation would silently never run and the card would be
                // left genuinely Active with the local row stuck Pending forever.
                var reread = await repository.GetAsync(tenantId, pending.Id, CancellationToken.None);
                if (reread is { Status: VisitorCredentialStatus.Granted })
                {
                    return reread; // confirmed: it actually committed despite the throw
                }

                await MarkFailedBestEffortAsync(pending, "local_commit_failed_after_card_active", seq);
            }
            else
            {
                // No card was ever confirmed issued under this attempt - nothing to compensate.
                await MarkFailedBestEffortAsync(pending, "card_issuance_failed", cardSequenceNumber: null);
            }

            throw;
        }
    }

    /// <summary>
    /// F01 fix, step 3: best-effort compensation for a grant attempt that did not complete - never
    /// lets the attempt vanish without a trace. See <see cref="GrantAsync"/>'s own header
    /// "Post-review remediation" for the full record.
    ///
    /// <b>F03 (Zainab's round-2 FAIL):</b> deliberately takes NO <see cref="CancellationToken"/>
    /// parameter at all - every external call in this method uses <see cref="CancellationToken.None"/>
    /// unconditionally, so a caller can never accidentally reintroduce the bug this method exists
    /// to fix by threading the original (possibly-already-cancelled) request token back in here.
    ///
    /// <b>F04 (Seyi's round-3 FAIL):</b> if the compensating deactivate did NOT confirm the card is
    /// no longer Active, AND the local <see cref="VisitorCredentialStatus.Failed"/>-transition
    /// write ALSO fails, this is exactly the pathological case
    /// <see cref="IStrandedCardLedger"/> exists for - a card that may still be genuinely Active
    /// with no local row anywhere that references it. Recorded there as the absolute last resort,
    /// so a LATER grant for the same person (possibly a different visitor) can never adopt it,
    /// even though this attempt's own local bookkeeping never converged.
    /// </summary>
    private async Task MarkFailedBestEffortAsync(VisitorCredential pending, string reason, int? cardSequenceNumber)
    {
        var cardConfirmedNotActive = cardSequenceNumber is null; // no card was ever issued under this attempt - nothing to compensate
        if (cardSequenceNumber is int seq)
        {
            try
            {
                var card = await cardGateway.GetAsync(pending.PersonId, pending.CardClass, seq, CancellationToken.None);
                if (card is { StatusWire: "active" })
                {
                    await cardGateway.DeactivateAsync(pending.PersonId, pending.CardClass, seq, card.Version, "system", "system", Guid.NewGuid().ToString(), CancellationToken.None);
                }

                cardConfirmedNotActive = true; // either it wasn't Active to begin with, or the deactivate above succeeded
            }
            catch
            {
                // Best effort only - the Failed-mark write below still runs regardless, so the
                // card's identifiers are recorded even if this compensating deactivate fails too.
                // cardConfirmedNotActive stays false: the card may still genuinely be Active.
            }
        }

        var localCommitSucceeded = false;
        try
        {
            var failed = pending.MarkFailed(reason, cardSequenceNumber, UtcInstant.Now());
            await repository.ReplaceAsync(pending, failed, CancellationToken.None);
            localCommitSucceeded = true;
        }
        catch
        {
            // Last resort below handles the case this leaves open.
        }

        if (!cardConfirmedNotActive && !localCommitSucceeded && cardSequenceNumber is int strandedSeq)
        {
            try
            {
                await strandedCardLedger.MarkStrandedAsync(pending.TenantId, pending.PersonId, pending.CardClass, strandedSeq, CancellationToken.None);
            }
            catch
            {
                // See IStrandedCardLedger's own header "Where hardening proportionately stops" -
                // this story does not chase a backstop for the backstop.
            }
        }
    }

    /// <summary>
    /// F05 fix (Seyi round-4 FAIL / Zainab round-3 FAIL, both independently flagging the same
    /// gap): <see cref="IStrandedCardLedger"/> (F04) only ever stopped a stranded card from being
    /// ADOPTED by a future grant - it did nothing about the card's own genuinely-<c>Active</c>
    /// state, so a failed grant could leave real, usable physical access indefinitely with no
    /// <see cref="VisitorCredentialStatus.Granted"/> credential history and no visitor-lifecycle
    /// path able to reach it (the <c>Pending</c> row has no card sequence attached in exactly this
    /// scenario, by construction). This retries the SAME compensating <c>DeactivateAsync</c>
    /// <see cref="MarkFailedBestEffortAsync"/> already attempted once, for every still-unresolved
    /// ledger entry for this <c>(tenantId, personId, cardClass)</c> - "a reconciliation path that
    /// repeatedly deactivates ledgered cards" and "recovery converges once dependencies resume" are
    /// both reviewers' own required-remediation words, verbatim. Rereads the card's current
    /// <see cref="CardIssueView.Version"/> immediately before each attempt rather than reusing one
    /// captured earlier - some other legitimate operation may have touched the card since.
    ///
    /// <b>Why reconciliation, not reaching into another story's access-decision layer.</b> Both
    /// reviewers also offered an alternative ("make the card/access-decision layer reject any
    /// ledgered card immediately"). That would mean this feature's own code reaching into
    /// BIO-ACC-002's or BIO-CRD-006's own domain - a cross-story change needing Kemi's recorded
    /// approval, and a weaker fix besides (the card would still show genuinely <c>Active</c> in
    /// BIO-CRD-005's own system of record; only a consumer that remembered to ALSO check this
    /// ledger would ever treat it as denied). Reconciliation instead uses a capability this
    /// feature already legitimately owns (<see cref="ICardIssuanceGateway.DeactivateAsync"/>) to
    /// make the card's own stored status true to reality - strictly more honest, and stays
    /// entirely within this feature's own ownership.
    ///
    /// <b>Trigger point.</b> Called opportunistically at the START of every <see cref="GrantAsync"/>
    /// for a person, under the same person-scoped lock already held there, so it can never race
    /// with another grant attempt for the same person. This codebase deliberately has no
    /// background-job infrastructure (every other derived-status evaluator in it is read-time-only
    /// for the same reason) - "the next time anyone touches this person's temporary-credential
    /// flow" is therefore the correct, consistent place to retry, not a cron job this synthetic
    /// system has no way to run. Declared <c>internal</c> (not <c>private</c>) so a test can invoke
    /// it directly without needing a full grant attempt.
    ///
    /// <b>Where this still cannot fully converge (updated post-F06).</b> Opportunistically, only a
    /// later grant/checkout/revoke for this exact person ever triggers a retry. That gap is now
    /// closed by <see cref="ReconcileStrandedCardsForPersonAsync"/>'s own dedicated operator
    /// endpoint (<c>POST /api/v1/visitor-credentials/reconcile</c>), which can be invoked on demand
    /// with no dependency on this person ever returning - see its own header. Deliberately uses
    /// <see cref="CancellationToken.None"/> throughout (the same F03 discipline
    /// <see cref="MarkFailedBestEffortAsync"/> applies) and never takes a
    /// <see cref="CancellationToken"/> parameter at all - a card may already genuinely be Active, so
    /// this recovery path must never be abortable by any caller's token.
    ///
    /// <b>F07 fix (Seyi round-7 FAIL / Zainab round-5 FAIL, both independently).</b> The audit write
    /// below used to be skipped entirely once a retry observed the card already inactive (mistaking
    /// "nothing left to neutralize" for "already audited"), so a deactivation that happened on an
    /// earlier attempt - whose own audit write then failed - could resolve the ledger with NO audit
    /// record ever written, permanently. Fixed: the audit write now always runs, whether
    /// deactivation happens in THIS attempt or already happened on a prior one, using a stable
    /// per-entry event id (<see cref="IStrandedCardLedger.GetOrCreateReconciliationEventIdAsync"/>)
    /// so a retry can never duplicate an audit record that a prior attempt already durably wrote.
    /// </summary>
    internal async Task ReconcileStrandedCardsAsync(string tenantId, string personId, string cardClass, AuditActor actor, string correlationId)
    {
        var unresolved = await strandedCardLedger.ListUnresolvedAsync(tenantId, personId, cardClass, CancellationToken.None);
        foreach (var sequenceNumber in unresolved)
        {
            try
            {
                var auditEventId = await strandedCardLedger.GetOrCreateReconciliationEventIdAsync(tenantId, personId, cardClass, sequenceNumber, CancellationToken.None);

                var card = await cardGateway.GetAsync(personId, cardClass, sequenceNumber, CancellationToken.None);
                if (card is { StatusWire: "active" })
                {
                    await cardGateway.DeactivateAsync(personId, cardClass, sequenceNumber, card.Version, "system", "system", Guid.NewGuid().ToString(), CancellationToken.None);
                }

                // F07 fix: always confirm/write the audit record before marking resolved - whether
                // deactivation just happened above, or a prior attempt already deactivated the card
                // and only failed to durably audit it. AuditAsync's own eventId parameter makes this
                // a no-op if a prior attempt's write already durably succeeded.
                await AuditAsync(tenantId, actor, "visitor_credential.stranded_card_reconciled", $"{personId}:{cardClass}:{sequenceNumber}", "stranded_card_reconciled", correlationId,
                    new Dictionary<string, (object?, string)>
                    {
                        ["person_id"] = (personId, "internal"),
                        ["card_class"] = (cardClass, "internal"),
                        ["card_sequence_number"] = (sequenceNumber, "internal"),
                    },
                    CancellationToken.None, eventId: auditEventId);

                await strandedCardLedger.MarkResolvedAsync(tenantId, personId, cardClass, sequenceNumber, CancellationToken.None);
            }
            catch
            {
                // Still unresolved (deactivation failed, or the audit write genuinely failed) -
                // leave it in the ledger for the next opportunity. This is the "repeatedly
                // deactivates" behavior both reviewers asked for: each subsequent attempt retries,
                // until BIO-CRD-005/the audit sink is available again.
            }
        }
    }

    /// <summary>
    /// F06 fix (Seyi's round-5 FAIL): a directly callable, permission-gated "operator recovery"
    /// entry point - the guaranteed-availability trigger Seyi's own review named explicitly,
    /// mapped as its own real HTTP endpoint (<c>POST /api/v1/visitor-credentials/reconcile</c>, see
    /// <c>VisitorCredentialEndpoints.cs</c>). Unlike <see cref="ReconcileStrandedCardsAsync"/>
    /// (opportunistic, only ever runs as a side effect of some OTHER operation for this person),
    /// this does not depend on a grant, checkout or revoke ever happening again for this person -
    /// it can be invoked on demand, any time, closing the "person who never returns" gap.
    /// Acquires this person's own lock for the duration, consistent with every other mutation this
    /// service makes against a person's cards.
    /// </summary>
    public async Task<ReconciliationResult> ReconcileStrandedCardsForPersonAsync(
        string personId, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();

        if (!await permissionGateway.ActorHasPermissionAsync(tenantId, actor.Id, ManagePermission, cancellationToken))
        {
            throw VisitorCredentialError.PermissionRequired(ManagePermission);
        }

        using var _ = await lockRegistry.AcquireAsync(tenantId, personId, cancellationToken);

        var before = await strandedCardLedger.ListUnresolvedAsync(tenantId, personId, CardClass, cancellationToken);
        await ReconcileStrandedCardsAsync(tenantId, personId, CardClass, actor, correlationId);
        var after = await strandedCardLedger.ListUnresolvedAsync(tenantId, personId, CardClass, cancellationToken);

        return new ReconciliationResult(Resolved: [.. before.Except(after)], StillUnresolved: after);
    }

    /// <summary>
    /// F04 fix (Seyi's round-3 FAIL): durably correlates <paramref name="pending"/> to
    /// <paramref name="sequenceNumber"/> as its OWN dedicated write, immediately once a card's
    /// identity is known - BEFORE activation, BEFORE the audit write, BEFORE the final
    /// <see cref="VisitorCredentialStatus.Granted"/>/<see cref="VisitorCredentialStatus.Failed"/>
    /// transition. This is what makes <see cref="IVisitorCredentialRepository.FindByCardAsync"/>
    /// able to attribute the card to this visitor even if everything after this point throws -
    /// closing the sequential "V1's attempt fails completely, V2 grants later and finds S
    /// unowned" scenario Seyi's round-3 review demonstrated, which the round-2 fix's
    /// end-of-attempt-only persistence did not.
    ///
    /// Uses <see cref="CancellationToken.None"/> unconditionally (F03's own discipline) and the
    /// same ambiguous-failure verification this codebase already uses for an audit write: if the
    /// CAS write itself throws, reread and check whether it actually landed despite the exception
    /// before concluding it genuinely failed.
    /// </summary>
    private async Task<VisitorCredential> AttachCardSequenceAsync(VisitorCredential pending, int sequenceNumber)
    {
        // Deliberately no CancellationToken parameter at all - the same F03 discipline
        // MarkFailedBestEffortAsync applies: a card may already genuinely exist by the time this
        // runs, so this write must never be abortable by the original request's own token.
        var withCard = pending with { CardSequenceNumber = sequenceNumber, Version = pending.Version + 1 };
        try
        {
            return await repository.ReplaceAsync(pending, withCard, CancellationToken.None);
        }
        catch (Exception)
        {
            var reread = await repository.GetAsync(pending.TenantId, pending.Id, CancellationToken.None);
            if (reread is { CardSequenceNumber: int attached } && attached == sequenceNumber)
            {
                return reread; // confirmed: it actually landed despite the throw
            }

            throw; // genuine failure - GrantAsync's own catch block handles recovery from here
        }
    }

    /// <summary>AC03 - an explicit, security-initiated early revoke (e.g. a badge reported lost). Gated identically to <see cref="GrantAsync"/>.</summary>
    public async Task<VisitorCredential> RevokeAsync(
        string credentialId, RevokeVisitorCredentialRequest request, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();

        if (!await permissionGateway.ActorHasPermissionAsync(tenantId, actor.Id, ManagePermission, cancellationToken))
        {
            throw VisitorCredentialError.PermissionRequired(ManagePermission);
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            throw VisitorCredentialError.Validation([new Api.ValidationErrorItem("reason", "required", "reason is required when revoking a credential")]);
        }

        var current = await repository.GetAsync(tenantId, credentialId, cancellationToken) ?? throw VisitorCredentialError.CredentialNotFound(credentialId);

        using var _ = await lockRegistry.AcquireAsync(tenantId, current.VisitorRecordId, cancellationToken);

        // Re-read under the lock: the row fetched above could already be stale by the time the
        // lock is acquired (e.g. a concurrent checkout-triggered revoke just ran).
        current = await repository.GetAsync(tenantId, credentialId, cancellationToken) ?? throw VisitorCredentialError.CredentialNotFound(credentialId);

        // F06 fix (Seyi's round-5 FAIL): widen the opportunistic reconciliation trigger beyond
        // GrantAsync - best-effort only, same discipline as GrantAsync's own call.
        try
        {
            await ReconcileStrandedCardsAsync(tenantId, current.PersonId, CardClass, actor, correlationId);
        }
        catch
        {
            // Still unresolved (or the ledger itself hiccupped) - the next opportunity retries.
        }

        if (request.Version is not int expectedVersion)
        {
            throw VisitorCredentialError.Validation([new Api.ValidationErrorItem("version", "required", "version is required so a stale write can be detected")]);
        }

        if (expectedVersion != current.Version)
        {
            throw VisitorCredentialError.ConcurrencyConflict(credentialId, expectedVersion, current.Version);
        }

        return await RevokeLockedAsync(tenantId, current, request.Reason!.Trim(), actor, correlationId, cancellationToken);
    }

    /// <summary>
    /// AC03 ("check-out revokes/ends credential") - this story's own orchestrating checkout.
    /// Calls through to BIO-VIS-001's own real <c>CheckOutAsync</c> via
    /// <see cref="IVisitorRecordGateway"/> (never reimplementing its own validation), then
    /// revokes whichever credential is currently Granted for this visitor, if any, deactivating
    /// its backing card too. This is why this story takes over the real
    /// <c>/api/v1/visitors/{visitorId}/check-out</c> route mapping from BIO-VIS-001's own
    /// (narrowed) <c>VisitorEndpoints.cs</c> - see
    /// <c>docs/features/bio-vis-002/README.md</c> "Why this story edits BIO-VIS-001's own
    /// endpoint mapping" for the full disclosed-handoff record.
    /// </summary>
    public async Task<(VisitorRecordView Visitor, VisitorCredential? RevokedCredential)> CheckOutAsync(
        string visitorId, int? expectedVersion, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        var actorTypeWire = actor.Type.ToWireString();

        using var _ = await lockRegistry.AcquireAsync(tenantId, visitorId, cancellationToken);

        var visitor = await visitorGateway.GetAsync(visitorId, cancellationToken) ?? throw VisitorCredentialError.VisitorNotFound(visitorId);

        // F06 fix (Seyi's round-5 FAIL): widen the opportunistic reconciliation trigger beyond
        // GrantAsync - any interaction with this person's visitor-credential flow is a chance to
        // self-heal a stranded card. Best-effort only, same discipline as GrantAsync's own call:
        // a reconciliation hiccup must never block an otherwise-valid checkout.
        if (visitor.PersonId is not null)
        {
            try
            {
                await ReconcileStrandedCardsAsync(tenantId, visitor.PersonId, CardClass, actor, correlationId);
            }
            catch
            {
                // Still unresolved (or the ledger itself hiccupped) - the next opportunity retries.
            }
        }

        // F02 fix: idempotent retry. If this visitor is already checked out - whether a prior run
        // of THIS orchestrator completed, or BIO-VIS-001's own direct checkout endpoint was used
        // with no credential ever granted - this is a no-op success, never a second call into
        // BIO-VIS-001's own one-way checkout (Seyi's own explicit "retries when the visitor is
        // already checked out" requirement). If a credential is somehow still Granted at this
        // point (this orchestrator crashed between its own two steps on a prior run), finish the
        // revoke now rather than leaving it stranded.
        if (visitor.VisitStatusWire == "checked-out")
        {
            var stillGranted = await repository.GetActiveForVisitorAsync(tenantId, visitorId, cancellationToken);
            if (stillGranted is null)
            {
                return (visitor, null);
            }

            var revokedNow = await RevokeLockedAsync(tenantId, stillGranted, "visitor_checked_out", actor, correlationId, cancellationToken);
            return (visitor, revokedNow);
        }

        // F02 fix: revoke BEFORE committing BIO-VIS-001's own checkout, not after - a visitor must
        // never be marked checked-out while still holding active physical access, not even
        // transiently. If this throws, BIO-VIS-001's own checkout below is never even attempted:
        // the visitor stays checked-in with access intact (safe, fail-closed, trivially
        // retryable) - the opposite of the original bug's fail-OPEN failure mode.
        var active = await repository.GetActiveForVisitorAsync(tenantId, visitorId, cancellationToken);
        VisitorCredential? revoked = null;
        if (active is not null)
        {
            revoked = await RevokeLockedAsync(tenantId, active, "visitor_checked_out", actor, correlationId, cancellationToken);
        }

        // Point of no return: only reached once any credential has genuinely been revoked.
        var checkedOut = await visitorGateway.CheckOutAsync(visitorId, expectedVersion, actor.Id, actorTypeWire, correlationId, cancellationToken);

        if (revoked is null)
        {
            await AuditAsync(tenantId, actor, "visitor_credential.checkout_orchestrated", visitorId, "visitor_checked_out_no_credential", correlationId,
                new Dictionary<string, (object?, string)> { ["visitor_record_id"] = (visitorId, "internal") }, cancellationToken);
        }

        return (checkedOut, revoked);
    }

    /// <summary>AC05 - a combined, human-readable account of one visitor's lifecycle plus every credential ever granted/revoked for them. A projection, not a new persisted aggregate.</summary>
    public async Task<(VisitorRecordView Visitor, IReadOnlyList<VisitorCredential> Credentials)> GetHistoryAsync(
        string visitorId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        var visitor = await visitorGateway.GetAsync(visitorId, cancellationToken) ?? throw VisitorCredentialError.VisitorNotFound(visitorId);
        var credentials = await repository.ListForVisitorAsync(tenantId, visitorId, cancellationToken);
        return (visitor, credentials);
    }

    /// <summary>Caller already holds the per-(tenant, visitor) lock and has re-read <paramref name="current"/> under it.</summary>
    private async Task<VisitorCredential> RevokeLockedAsync(
        string tenantId, VisitorCredential current, string reason, AuditActor actor, string correlationId, CancellationToken cancellationToken)
    {
        if (current.Status == VisitorCredentialStatus.Revoked)
        {
            return current; // already revoked (e.g. a racing manual revoke lost to the checkout orchestrator): no-op, nothing to audit twice.
        }

        if (current.Status != VisitorCredentialStatus.Granted)
        {
            // Only a Granted credential was ever actually backed by an Active card to revoke -
            // Pending/Failed never reached that state (BIO-VIS-002-F01's own remediation).
            throw VisitorCredentialError.InvalidTransition(current.Id, current.Status, VisitorCredentialStatus.Revoked);
        }

        var now = UtcInstant.Now();
        var updated = current.Revoke(reason, now);
        var cardSequenceNumber = current.CardSequenceNumber ?? throw new InvalidOperationException(
            $"a Granted visitor credential must always have a known card sequence number: {current.Id}");

        var actorTypeWire = actor.Type.ToWireString();
        var card = await cardGateway.GetAsync(current.PersonId, current.CardClass, cardSequenceNumber, cancellationToken);
        if (card is not null && card.StatusWire == "active")
        {
            // Idempotent against a racing revoke that already deactivated the same card: a
            // non-Active card is simply not touched again, rather than failing the whole revoke.
            await cardGateway.DeactivateAsync(current.PersonId, current.CardClass, cardSequenceNumber, card.Version, actor.Id, actorTypeWire, correlationId, cancellationToken);
        }

        await AuditAsync(tenantId, actor, "visitor_credential.revoked", current.Id, "visitor_credential_revoked", correlationId,
            new Dictionary<string, (object?, string)>
            {
                ["visitor_record_id"] = (current.VisitorRecordId, "internal"),
                ["reason"] = (reason, "internal"),
            },
            cancellationToken);

        return await repository.ReplaceAsync(current, updated, CancellationToken.None);
    }

    /// <summary>
    /// F07's own remediation (Seyi round-7 FAIL / Zainab round-5 FAIL): <paramref name="eventId"/>
    /// is optional and purely additive - every existing call site (none pass it) behaves exactly as
    /// before, via <see cref="IVisitorCredentialAuditSink.WriteAsync"/> below.
    ///
    /// <b>F08 fix (Seyi round-8 FAIL / Zainab round-6 FAIL, both independently).</b> When a caller
    /// DOES pass a stable id (<see cref="ReconcileStrandedCardsAsync"/>, so a retry across separate
    /// top-level attempts never duplicates or loses this exact audit record), the original
    /// check-then-write (<see cref="IVisitorCredentialAuditSink.ExistsAsync"/> then
    /// <see cref="IVisitorCredentialAuditSink.WriteAsync"/>) raced: two concurrent callers with the
    /// same stable id could both observe "absent" before either wrote, since reconciliation is
    /// triggered from call paths that do not all share one lock (grant/the operator endpoint hold
    /// the tenant/person lock; checkout/revoke hold a visitor-record lock instead). The later
    /// plain-dictionary <see cref="IVisitorCredentialAuditSink.WriteAsync"/> would then silently
    /// clobber the earlier writer's payload - an append-only audit record must never be mutable.
    /// Fixed by routing the stable-id path through
    /// <see cref="IVisitorCredentialAuditSink.CreateIfAbsentAsync"/> instead, whose own atomicity
    /// (a single <c>ConcurrentDictionary.GetOrAdd</c>) makes the race impossible rather than merely
    /// narrowing its window - deliberately NOT a lock-unification redesign, which would widen the
    /// blast radius of locks this service already uses correctly for their own, narrower purposes.
    /// </summary>
    private async Task AuditAsync(
        string tenantId, AuditActor actor, string action, string targetId, string reasonCode, string correlationId,
        IReadOnlyDictionary<string, (object? Value, string Classification)> detail, CancellationToken cancellationToken,
        string? eventId = null)
    {
        if (eventId is not null)
        {
            var auditEvent = VisitorCredentialAuditEventBuilder.Build(
                tenantId, actor, action, new AuditTarget("visitor_credential", targetId), AuditOutcome.Allowed, reasonCode, correlationId, null, detail) with { EventId = eventId };

            try
            {
                await auditSink.CreateIfAbsentAsync(auditEvent, CancellationToken.None);
            }
            catch
            {
                if (!await auditSink.ExistsAsync(eventId, CancellationToken.None))
                {
                    throw VisitorCredentialError.AuditUnavailable();
                }
            }

            return;
        }

        var freshAuditEvent = VisitorCredentialAuditEventBuilder.Build(
            tenantId, actor, action, new AuditTarget("visitor_credential", targetId), AuditOutcome.Allowed, reasonCode, correlationId, null, detail);

        try
        {
            await auditSink.WriteAsync(freshAuditEvent, cancellationToken);
        }
        catch
        {
            if (!await auditSink.ExistsAsync(freshAuditEvent.EventId, CancellationToken.None))
            {
                throw VisitorCredentialError.AuditUnavailable();
            }
        }
    }

    private string RequireTenant() => tenantProvider.TenantId ?? throw VisitorCredentialError.TenantContextMissing();
}
