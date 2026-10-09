using System.Collections.Concurrent;

namespace Biolog.Features.ImportConflictReview.Domain;

/// <summary>
/// A single, process-lifetime, cross-tenant dictionary standing in for the per-tenant database
/// ADR-0002 Decision 4.1 describes, keyed by <c>(TenantId, MappingId, ExternalKey)</c> - the
/// same simplification every sibling in-memory repository already draws around itself. See
/// <see cref="IImportConflictReviewRepository"/> for why this class is <c>internal</c> and what
/// belt-and-braces validation it applies independently of <see cref="ImportConflictReviewService"/>.
/// </summary>
internal sealed class InMemoryImportConflictReviewRepository : IImportConflictReviewRepository
{
    private readonly ConcurrentDictionary<(string TenantId, string MappingId, string ExternalKey), ImportConflictReview> _reviews = new();

    public Task<ImportConflictReview?> GetAsync(string tenantId, string mappingId, string externalKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _reviews.TryGetValue((tenantId, mappingId, externalKey), out var review);
        return Task.FromResult(review);
    }

    public Task<IReadOnlyList<ImportConflictReview>> ListForMappingAsync(string tenantId, string mappingId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<ImportConflictReview> items =
        [
            .. _reviews.Values
                .Where(r => r.TenantId == tenantId && r.MappingId == mappingId)
                .OrderBy(r => r.CreatedAt, StringComparer.Ordinal),
        ];
        return Task.FromResult(items);
    }

    public Task<ImportConflictReview> CreateAsync(ImportConflictReview review, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (review.Status != ImportConflictReviewStatus.PendingReview || review.Version != 1)
        {
            throw ImportConflictReviewError.InvalidInitialState(review.MappingId, review.ExternalKey, review.Status, review.Version);
        }

        var key = (review.TenantId, review.MappingId, review.ExternalKey);
        if (!_reviews.TryAdd(key, review))
        {
            // A genuine natural-key collision - never legitimately reachable via
            // ImportConflictReviewService, which always checks for an existing record under
            // the same per-row lock before ever constructing a new one.
            throw ImportConflictReviewError.InvalidInitialState(review.MappingId, review.ExternalKey, review.Status, review.Version);
        }

        return Task.FromResult(review);
    }

    public Task<ImportConflictReview> ReplaceAsync(ImportConflictReview expected, ImportConflictReview updated, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = (expected.TenantId, expected.MappingId, expected.ExternalKey);
        if (!_reviews.TryGetValue(key, out var current) || current.TenantId != expected.TenantId)
        {
            throw ImportConflictReviewError.NotFound(expected.MappingId, expected.ExternalKey);
        }

        if (updated.TenantId != expected.TenantId || updated.MappingId != expected.MappingId || updated.ExternalKey != expected.ExternalKey)
        {
            // Unreachable via `expected with { ... }` (these fields are get-only) - catches
            // two unrelated instances being passed together, mirroring every sibling
            // repository's identical dual-boundary check.
            throw ImportConflictReviewError.IdentityImmutable(expected.MappingId, expected.ExternalKey);
        }

        // Structural (record) equality: succeeds only if nothing has changed since the caller
        // read `expected` - the CAS this feature's optimistic concurrency depends on.
        if (!_reviews.TryUpdate(key, updated, expected))
        {
            var latest = _reviews.TryGetValue(key, out var c) ? c : current;
            throw ImportConflictReviewError.ConcurrencyConflict(expected.MappingId, expected.ExternalKey, expected.Version, latest.Version);
        }

        return Task.FromResult(updated);
    }
}
