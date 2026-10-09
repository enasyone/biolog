using System.Collections.Concurrent;

namespace Biolog.Features.DuplicateReview.Domain;

/// <summary>
/// A single, process-lifetime, cross-tenant dictionary standing in for the per-tenant database
/// ADR-0002 Decision 4.1 describes, keyed by <c>(TenantId, PersonIdLow, PersonIdHigh)</c> - the
/// same simplification every sibling in-memory repository already draws around itself. See
/// <see cref="IDuplicateCandidateRepository"/> for why this class is <c>internal</c> and what
/// belt-and-braces validation it applies independently of <see cref="DuplicateReviewService"/>.
/// </summary>
internal sealed class InMemoryDuplicateCandidateRepository : IDuplicateCandidateRepository
{
    private readonly ConcurrentDictionary<(string TenantId, string PersonIdLow, string PersonIdHigh), DuplicateCandidate> _candidates = new();

    public Task<DuplicateCandidate?> GetAsync(string tenantId, string personIdLow, string personIdHigh, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _candidates.TryGetValue((tenantId, personIdLow, personIdHigh), out var candidate);
        return Task.FromResult(candidate);
    }

    public Task<IReadOnlyList<DuplicateCandidate>> ListForPersonAsync(string tenantId, string personId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<DuplicateCandidate> items =
        [
            .. _candidates.Values
                .Where(c => c.TenantId == tenantId && (c.PersonIdLow == personId || c.PersonIdHigh == personId))
                .OrderBy(c => c.CreatedAt, StringComparer.Ordinal),
        ];
        return Task.FromResult(items);
    }

    public Task<DuplicateCandidate> CreateAsync(DuplicateCandidate candidate, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (candidate.Status != DuplicateReviewStatus.PendingReview || candidate.Version != 1)
        {
            throw DuplicateCandidateError.InvalidInitialState(candidate.PersonIdLow, candidate.PersonIdHigh, candidate.Status, candidate.Version);
        }

        var key = (candidate.TenantId, candidate.PersonIdLow, candidate.PersonIdHigh);
        if (!_candidates.TryAdd(key, candidate))
        {
            // A genuine natural-key collision - never legitimately reachable via
            // DuplicateReviewService, which always checks for an existing record under the
            // same per-pair lock before ever constructing a new one.
            throw DuplicateCandidateError.InvalidInitialState(candidate.PersonIdLow, candidate.PersonIdHigh, candidate.Status, candidate.Version);
        }

        return Task.FromResult(candidate);
    }

    public Task<DuplicateCandidate> ReplaceAsync(DuplicateCandidate expected, DuplicateCandidate updated, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = (expected.TenantId, expected.PersonIdLow, expected.PersonIdHigh);
        if (!_candidates.TryGetValue(key, out var current) || current.TenantId != expected.TenantId)
        {
            throw DuplicateCandidateError.NotFound(expected.TenantId, expected.PersonIdLow, expected.PersonIdHigh);
        }

        if (updated.TenantId != expected.TenantId || updated.PersonIdLow != expected.PersonIdLow || updated.PersonIdHigh != expected.PersonIdHigh)
        {
            // Unreachable via `expected with { ... }` (these fields are get-only) - catches
            // two unrelated instances being passed together, mirroring every sibling
            // repository's identical dual-boundary check.
            throw DuplicateCandidateError.IdentityImmutable(expected.PersonIdLow, expected.PersonIdHigh);
        }

        // Structural (record) equality: succeeds only if nothing has changed since the caller
        // read `expected` - the CAS this feature's optimistic concurrency depends on.
        if (!_candidates.TryUpdate(key, updated, expected))
        {
            var latest = _candidates.TryGetValue(key, out var c) ? c : current;
            throw DuplicateCandidateError.ConcurrencyConflict(expected.PersonIdLow, expected.PersonIdHigh, expected.Version, latest.Version);
        }

        return Task.FromResult(updated);
    }
}
