namespace Biolog.Features.PhotoQuality.Domain;

/// <summary>
/// Storage for <see cref="PhotoQualityCapture"/>, keyed by <c>(TenantId, PersonId, SequenceNumber)</c>.
///
/// <b><c>internal</c>, from the start</b> - same rationale as every sibling story's own
/// repository (BIO-IDN-001-QA-01's lesson).
///
/// <b>Decided is terminal, enforced here too.</b> <see cref="ReplaceAsync"/> requires
/// <paramref name="expectedCurrent"/>'s own <see cref="PhotoQualityCapture.Status"/> to be
/// <see cref="PhotoQualityCaptureStatus.Pending"/> - a caller that somehow obtained a stale
/// reference to an already-decided capture cannot replace it again, mirroring
/// <c>IBoothCaptureRepository</c>'s (BIO-PHO-002) identical enforcement. The service layer
/// never needs to be trusted alone to enforce this.
/// </summary>
internal interface IPhotoQualityCaptureRepository
{
    Task<PhotoQualityCapture?> GetAsync(string tenantId, string personId, int sequenceNumber, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PhotoQualityCapture>> ListForPersonAsync(string tenantId, string personId, CancellationToken cancellationToken = default);

    /// <summary>The first-ever capture at this (TenantId, PersonId, SequenceNumber) - throws if one already exists.</summary>
    Task<PhotoQualityCapture> CreateAsync(PhotoQualityCapture capture, CancellationToken cancellationToken = default);

    /// <summary>Compare-and-set against <paramref name="expectedCurrent"/> (ADR-0001 §3.2). Throws unless <paramref name="expectedCurrent"/>.Status is PENDING.</summary>
    Task<PhotoQualityCapture> ReplaceAsync(PhotoQualityCapture expectedCurrent, PhotoQualityCapture updated, CancellationToken cancellationToken = default);
}
