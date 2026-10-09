namespace Biolog.Features.DuplicateReview.Domain;

/// <summary>
/// Storage for <see cref="PersonContactPoint"/>, keyed by <c>(TenantId, PersonId)</c>.
/// <c>internal</c> from the start (BIO-IDN-001-QA-01's lesson) - <c>InternalsVisibleTo</c> grants
/// access to <c>Biolog.Api</c> (DI registration) and this project's own test assembly.
/// <see cref="InMemoryPersonContactPointRepository"/> independently re-validates identity
/// immutability at the persistence boundary - the same dual-boundary discipline every other
/// repository in this project applies.
///
/// Unlike <see cref="IDuplicateCandidateRepository"/>, <see cref="DuplicateReviewService"/>
/// reads this repository DIRECTLY (no gateway/adapter seam) - <see cref="PersonContactPoint"/>
/// is owned by this SAME feature project, not a sibling story's store, so the "narrow read-only
/// gateway, no ProjectReference" pattern that seam exists for does not apply here.
/// </summary>
internal interface IPersonContactPointRepository
{
    Task<PersonContactPoint?> GetAsync(string tenantId, string personId, CancellationToken cancellationToken = default);

    /// <summary>Every contact point on file for this tenant - used by a scan to compare the subject against everyone else's on-file contact data.</summary>
    Task<IReadOnlyList<PersonContactPoint>> ListByTenantAsync(string tenantId, CancellationToken cancellationToken = default);

    Task<PersonContactPoint> CreateAsync(PersonContactPoint contactPoint, CancellationToken cancellationToken = default);

    /// <summary>Compare-and-set against <paramref name="expected"/> (ADR-0001 Decision 3.2).</summary>
    Task<PersonContactPoint> ReplaceAsync(PersonContactPoint expected, PersonContactPoint updated, CancellationToken cancellationToken = default);
}
