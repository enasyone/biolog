namespace Biolog.Features.CardLifecycle.Domain;

/// <summary>Decoupled mirror of BIO-CRD-007's own <c>ProductionJobStatus</c> - the same pattern <see cref="CardIssueLifecycleStatus"/> already uses for BIO-CRD-005.</summary>
public enum ProductionJobLifecycleStatus
{
    InProgress,
    Passed,
    Failed,
    Rejected,
    Cancelled,
}

/// <summary>A read-only projection of BIO-CRD-007's latest production job for one card issue - only what this feature's own activation precondition needs.</summary>
public sealed record ProductionStatusSnapshot
{
    /// <summary>True only if the latest production job for this card issue is <see cref="ProductionJobLifecycleStatus.Passed"/> - mirrors BIO-CRD-007's own <c>AllowsActivation</c>, the only status that may ever gate an activation.</summary>
    public required bool HasPassed { get; init; }

    public int? LatestJobSequenceNumber { get; init; }
    public ProductionJobLifecycleStatus? LatestStatus { get; init; }
}

/// <summary>
/// BIO-OPS-003-AC04 ("production failure cannot activate a card"). Seyi's acceptance run found
/// that neither BIO-CRD-006's own replacement activation nor BIO-CRD-007's own
/// production-gated activation composed a check of BOTH "production passed" AND "earlier cards
/// revoked" at the one endpoint that actually performs a REPLACEMENT activation - BIO-CRD-007's
/// own <c>ActivateAfterProductionAsync</c> checks production but never sweeps/revokes earlier
/// cards (that is this story's own job), and this story's own
/// <see cref="CardLifecycle.CardLifecycleService.ActivateReplacementAsync"/> ran the sweep and
/// activated without ever consulting production status at all.
///
/// Kemi's ruling: the fix belongs entirely here, in BIO-CRD-006, which already owns the hard,
/// compensation-protected revocation transaction (see
/// <see cref="CardLifecycle.CardLifecycleService"/>'s header, QA-01 through QA-03) - BIO-CRD-007
/// needs zero changes, since its own <c>ProductionService.ListJobsAsync</c> is already public.
/// This feature's own narrow seam onto that real, already-shipped production-job record - the
/// same shape <see cref="ICardIssueGateway"/> already established for BIO-CRD-005. The real
/// implementation is an adapter in <c>Biolog.Api.Modules.BioCrd006Module</c> wrapping
/// BIO-CRD-007's own already-registered <c>ProductionService.ListJobsAsync</c> directly.
/// </summary>
public interface IProductionStatusGateway
{
    Task<ProductionStatusSnapshot> HasPassedAsync(string personId, string cardClass, int sequenceNumber, CancellationToken cancellationToken = default);
}
