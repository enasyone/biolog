namespace Biolog.Features.PhotoBooth.Domain;

/// <summary>
/// Storage for <see cref="BoothCapture"/>, keyed by <c>(PersonId, SequenceNumber)</c>.
///
/// <b><c>internal</c>, from the start</b> - same rationale as every sibling story's own
/// repository (BIO-IDN-001-QA-01's lesson).
///
/// <b>Decided is terminal, enforced here too.</b> <see cref="ReplaceAsync"/> requires
/// <paramref name="expectedCurrent"/>'s own <see cref="BoothCapture.Status"/> to be
/// <see cref="CaptureStatus.Pending"/> - a caller that somehow obtained a stale reference to an
/// already-decided capture cannot replace it again, mirroring
/// <c>ApprovalRequest</c>'s (BIO-IDN-006) "decided requests are terminal" repository-boundary
/// enforcement. The service layer never needs to be trusted alone to enforce this.
/// </summary>
internal interface IBoothCaptureRepository
{
    Task<BoothCapture?> GetAsync(string tenantId, string personId, int sequenceNumber, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BoothCapture>> ListForPersonAsync(string tenantId, string personId, CancellationToken cancellationToken = default);

    /// <summary>The first-ever capture at this (PersonId, SequenceNumber) - throws if one already exists.</summary>
    Task<BoothCapture> CreateAsync(BoothCapture capture, CancellationToken cancellationToken = default);

    /// <summary>Compare-and-set against <paramref name="expectedCurrent"/> (ADR-0001 §3.2). Throws unless <paramref name="expectedCurrent"/>.Status is PENDING.</summary>
    Task<BoothCapture> ReplaceAsync(BoothCapture expectedCurrent, BoothCapture updated, CancellationToken cancellationToken = default);
}
