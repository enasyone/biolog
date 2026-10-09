namespace Biolog.Features.VisitorCredentials.Domain;

/// <summary>
/// BIO-VIS-002-F04's own remediation (Seyi's round-3 FAIL, <c>evidence/BIO-VIS-002/codex/REVIEW-Seyi-round3.md</c>).
///
/// <b>The gap this closes.</b> <see cref="IVisitorCredentialRepository.FindByCardAsync"/> can only
/// attribute a card to a visitor once a credential row durably records that card's sequence
/// number. <see cref="VisitorCredentialService.GrantAsync"/> now persists that correlation onto the
/// still-<see cref="VisitorCredentialStatus.Pending"/> row as its OWN early, dedicated write,
/// immediately once a card's identity is known (before activation, before audit, before the final
/// <see cref="VisitorCredentialStatus.Granted"/>/<see cref="VisitorCredentialStatus.Failed"/>
/// transition) - closing the overwhelming majority of real failures. This ledger is the last-resort
/// backstop for the genuinely pathological remainder: that early write itself fails AND the
/// best-effort compensating card deactivation also fails, leaving a card that may still be
/// genuinely Active with NO local row anywhere that references it. Once recorded here, that exact
/// <c>(PersonId, CardClass, SequenceNumber)</c> is excluded from adoption by ANY future grant for
/// ANY visitor, permanently - independent of whatever <see cref="IVisitorCredentialRepository"/>
/// ends up containing.
///
/// <b>Where hardening proportionately stops.</b> This store is deliberately as simple and
/// low-failure-surface as every other in-memory store in this codebase (a dictionary write, no
/// external dependency). If THIS write also failed, the system would need a backstop for the
/// backstop - an infinite regress that is not proportionate for a synthetic, in-memory system with
/// no real persistence tier. See <see cref="VisitorCredentialService"/>'s own
/// <c>MarkFailedBestEffortAsync</c> for exactly when this is reached, and
/// <c>docs/features/bio-vis-002/README.md</c> "Post-review remediation" round 4 for the full record.
///
/// <b>BIO-VIS-002-F05 (Seyi round-4 FAIL / Zainab round-3 FAIL, both independently).</b> Recording
/// a card here only ever stopped it being ADOPTED by a future grant - it did nothing about the
/// card's own genuinely-Active state, so a failed grant could leave real, usable physical access
/// indefinitely. <see cref="ListUnresolvedAsync"/>/<see cref="MarkResolvedAsync"/> exist so
/// <see cref="VisitorCredentialService.ReconcileStrandedCardsAsync"/> can repeatedly retry actually
/// deactivating a ledgered card until it succeeds - "resolved" here means "confirmed no longer
/// Active", not merely "excluded from adoption". <see cref="IsStrandedAsync"/> only reports entries
/// that are BOTH recorded AND still unresolved - once reconciliation confirms a card is genuinely
/// neutralized, it stops being a special case for anything (adoption's `ListAsync` filter already
/// excludes a non-issued/active card on its own).
/// </summary>
internal interface IStrandedCardLedger
{
    Task MarkStrandedAsync(string tenantId, string personId, string cardClass, int sequenceNumber, CancellationToken cancellationToken = default);

    /// <summary>True only while the entry is both recorded and still unresolved (see this interface's own header).</summary>
    Task<bool> IsStrandedAsync(string tenantId, string personId, string cardClass, int sequenceNumber, CancellationToken cancellationToken = default);

    /// <summary>F05's own remediation - every sequence number still awaiting successful reconciliation for this (tenant, person, card class), used by <see cref="VisitorCredentialService.ReconcileStrandedCardsAsync"/>. Empty if none.</summary>
    Task<IReadOnlyList<int>> ListUnresolvedAsync(string tenantId, string personId, string cardClass, CancellationToken cancellationToken = default);

    /// <summary>F05's own remediation - marks a ledger entry as successfully reconciled (the backing card has been confirmed deactivated, or was already not Active). No-op if the entry does not exist or is already resolved.</summary>
    Task MarkResolvedAsync(string tenantId, string personId, string cardClass, int sequenceNumber, CancellationToken cancellationToken = default);

    /// <summary>
    /// F07's own remediation (Seyi round-7 FAIL / Zainab round-5 FAIL, both independently).
    /// <see cref="VisitorCredentialService.ReconcileStrandedCardsAsync"/> can span several separate
    /// top-level attempts (a grant, a checkout, a revoke, the dedicated reconcile endpoint) before it
    /// finally succeeds. Mints a <c>evt_</c> audit event id the FIRST time this exact
    /// (tenant, person, card class, sequence number) entry asks for one, persists it, and returns
    /// that SAME id on every later call for that key - giving the entry's own reconciliation audit a
    /// stable identity across retries, so the audit write can be deterministically retried (or
    /// recognised as already durably written) without ever risking two different ids describing the
    /// same physical deactivation, or the audit being silently skipped once the card is already
    /// inactive from a prior attempt. One more dictionary field, same proportionality stance as the
    /// rest of this ledger - no new infrastructure.
    /// </summary>
    Task<string> GetOrCreateReconciliationEventIdAsync(string tenantId, string personId, string cardClass, int sequenceNumber, CancellationToken cancellationToken = default);
}
