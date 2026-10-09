using System.Collections.Concurrent;

namespace Biolog.Features.DuplicateReview.Domain;

/// <summary>
/// A single, process-lifetime, cross-tenant dictionary standing in for the per-tenant database
/// ADR-0002 Decision 4.1 describes, keyed by <c>(TenantId, PersonId)</c>. See
/// <see cref="IPersonContactPointRepository"/> for why this class is <c>internal</c> and what
/// belt-and-braces validation it applies independently of
/// <see cref="PersonContactPointService"/>.
/// </summary>
internal sealed class InMemoryPersonContactPointRepository : IPersonContactPointRepository
{
    private readonly ConcurrentDictionary<(string TenantId, string PersonId), PersonContactPoint> _contactPoints = new();

    public Task<PersonContactPoint?> GetAsync(string tenantId, string personId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _contactPoints.TryGetValue((tenantId, personId), out var contactPoint);
        return Task.FromResult(contactPoint);
    }

    public Task<IReadOnlyList<PersonContactPoint>> ListByTenantAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<PersonContactPoint> items = [.. _contactPoints.Values.Where(c => c.TenantId == tenantId)];
        return Task.FromResult(items);
    }

    public Task<PersonContactPoint> CreateAsync(PersonContactPoint contactPoint, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (contactPoint.Version != 1)
        {
            throw DuplicateCandidateError.InvalidContactPointInitialState(contactPoint.PersonId, contactPoint.Version);
        }

        var key = (contactPoint.TenantId, contactPoint.PersonId);
        if (!_contactPoints.TryAdd(key, contactPoint))
        {
            // A genuine natural-key collision - never legitimately reachable via
            // PersonContactPointService, which always checks for an existing record under the
            // same per-person lock before ever constructing a new one.
            throw DuplicateCandidateError.InvalidContactPointInitialState(contactPoint.PersonId, contactPoint.Version);
        }

        return Task.FromResult(contactPoint);
    }

    public Task<PersonContactPoint> ReplaceAsync(PersonContactPoint expected, PersonContactPoint updated, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = (expected.TenantId, expected.PersonId);
        if (!_contactPoints.TryGetValue(key, out var current) || current.TenantId != expected.TenantId)
        {
            throw DuplicateCandidateError.ContactPointNotFound(expected.PersonId);
        }

        if (updated.TenantId != expected.TenantId || updated.PersonId != expected.PersonId)
        {
            // Unreachable via `expected with { ... }` (these fields are get-only) - catches two
            // unrelated instances being passed together, mirroring every sibling repository's
            // identical dual-boundary check.
            throw DuplicateCandidateError.ContactPointIdentityImmutable(expected.PersonId);
        }

        if (!_contactPoints.TryUpdate(key, updated, expected))
        {
            var latest = _contactPoints.TryGetValue(key, out var c) ? c : current;
            throw DuplicateCandidateError.ContactPointConcurrencyConflict(expected.PersonId, expected.Version, latest.Version);
        }

        return Task.FromResult(updated);
    }
}
