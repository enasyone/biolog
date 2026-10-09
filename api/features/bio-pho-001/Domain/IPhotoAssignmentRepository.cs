namespace Biolog.Features.Photography.Domain;

/// <summary>
/// Storage for <see cref="PhotoAssignment"/>, keyed by <c>PersonId</c>. <c>internal</c>, from
/// the start - same rationale as <see cref="IPhotoMasterRepository"/>.
/// </summary>
internal interface IPhotoAssignmentRepository
{
    Task<PhotoAssignment?> GetAsync(string tenantId, string personId, CancellationToken cancellationToken = default);

    /// <summary>The first-ever assignment for this person. Throws if one already exists (the caller should use <see cref="ReplaceAsync"/> instead).</summary>
    Task<PhotoAssignment> CreateAsync(PhotoAssignment assignment, CancellationToken cancellationToken = default);

    /// <summary>Compare-and-set against <paramref name="expectedCurrent"/> (ADR-0001 §3.2).</summary>
    Task<PhotoAssignment> ReplaceAsync(PhotoAssignment expectedCurrent, PhotoAssignment updated, CancellationToken cancellationToken = default);
}
