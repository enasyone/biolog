namespace Biolog.Features.DuplicateReview.Domain;

/// <summary>
/// Storage for <see cref="DuplicateCandidate"/>, keyed by
/// <c>(TenantId, PersonIdLow, PersonIdHigh)</c>. <c>internal</c> from the start
/// (BIO-IDN-001-QA-01's lesson) - <c>InternalsVisibleTo</c> in this project's <c>.csproj</c>
/// grants access to <c>Biolog.Api</c> (DI registration) and this project's own test assembly.
/// <see cref="InMemoryDuplicateCandidateRepository"/> independently re-validates identity
/// immutability and initial state at the persistence boundary - the same dual-boundary
/// discipline every sibling repository in this epic applies.
///
/// Every read and write method takes <c>tenantId</c> explicitly and asserts a matching record's
/// own <see cref="DuplicateCandidate.TenantId"/> before returning it - BIO-IDN-005-AC04's
/// tenant isolation, enforced at this boundary too, not only at the gateways that supply
/// candidate signals.
/// </summary>
internal interface IDuplicateCandidateRepository
{
    Task<DuplicateCandidate?> GetAsync(string tenantId, string personIdLow, string personIdHigh, CancellationToken cancellationToken = default);

    /// <summary>Every candidate involving this person, in either pair position, tenant-scoped - AC02's review queue.</summary>
    Task<IReadOnlyList<DuplicateCandidate>> ListForPersonAsync(string tenantId, string personId, CancellationToken cancellationToken = default);

    Task<DuplicateCandidate> CreateAsync(DuplicateCandidate candidate, CancellationToken cancellationToken = default);

    /// <summary>Compare-and-set against <paramref name="expected"/> (ADR-0001 Decision 3.2).</summary>
    Task<DuplicateCandidate> ReplaceAsync(DuplicateCandidate expected, DuplicateCandidate updated, CancellationToken cancellationToken = default);
}
