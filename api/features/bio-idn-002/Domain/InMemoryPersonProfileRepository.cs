using System.Collections.Concurrent;

namespace Biolog.Features.PersonProfileRegistry.Domain;

/// <summary>
/// A single, process-lifetime, cross-tenant dictionary standing in for the per-tenant
/// database ADR-0002 Decision 4.1 describes, keyed by <c>(PersonId, Type)</c> - the same
/// simplification every sibling in-memory repository already draws around itself. See
/// <see cref="IPersonProfileRepository"/> for why this class is <c>internal</c> and what
/// belt-and-braces validation it applies independently of <see cref="PersonProfileService"/>.
/// </summary>
internal sealed class InMemoryPersonProfileRepository : IPersonProfileRepository
{
    private readonly ConcurrentDictionary<(string PersonId, ProfileType Type), PersonProfile> _profiles = new();

    public Task<PersonProfile> CreateAsync(PersonProfile profile, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (profile.Status != ProfileStatus.Active || profile.Version != 1)
        {
            throw PersonProfileError.InvalidInitialState(profile.PersonId, profile.Type, profile.Status, profile.Version);
        }

        var key = (profile.PersonId, profile.Type);
        if (!_profiles.TryAdd(key, profile))
        {
            // AC01/AC04's real enforcement point: at most one profile per (person, type).
            throw PersonProfileError.DuplicateProfileType(profile.PersonId, profile.Type);
        }

        return Task.FromResult(profile);
    }

    public Task<PersonProfile?> GetAsync(string tenantId, string personId, ProfileType type, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_profiles.TryGetValue((personId, type), out var profile) || profile.TenantId != tenantId)
        {
            // Cross-tenant lookup returns null (mapped to 404 by the caller), never a
            // distinct "exists but forbidden" signal - ADR-0001 §1.5.
            return Task.FromResult<PersonProfile?>(null);
        }

        return Task.FromResult<PersonProfile?>(profile);
    }

    public Task<IReadOnlyList<PersonProfile>> ListByPersonAsync(
        string tenantId, string personId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<PersonProfile> items =
        [
            .. _profiles.Values
                .Where(p => p.TenantId == tenantId && p.PersonId == personId)
                .OrderBy(p => p.Type.ToWireString(), StringComparer.Ordinal),
        ];
        return Task.FromResult(items);
    }

    public Task<PersonProfile> ReplaceAsync(PersonProfile expected, PersonProfile updated, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = (expected.PersonId, expected.Type);
        if (!_profiles.TryGetValue(key, out var current) || current.TenantId != expected.TenantId)
        {
            throw PersonProfileError.NotFound(expected.PersonId, expected.Type);
        }

        if (updated.PersonId != expected.PersonId || updated.TenantId != expected.TenantId || updated.Type != expected.Type)
        {
            // BIO-IDN-002-QA-01-preempted: identity may never move via a replace - see class
            // header. Unreachable via `expected with { ... }` (PersonId/TenantId/Type are
            // get-only), so this catches two unrelated PersonProfile instances being passed
            // together, not merely a mutated copy of one.
            throw PersonProfileError.IdentityImmutable(expected.PersonId, expected.Type);
        }

        if (expected.Status == ProfileStatus.Revoked)
        {
            // The only governed transition is Active -> Revoked, and it is terminal - no
            // legitimate caller ever reaches ReplaceAsync a second time for an already-
            // revoked profile (PersonProfileService returns early on that case before ever
            // calling this method), so this is unreachable via any legitimate call and exists
            // purely as the repository-boundary half of the dual-boundary discipline.
            throw PersonProfileError.AlreadyRevoked(expected.PersonId, expected.Type);
        }

        // Structural (record) equality: succeeds only if nothing has changed since the
        // caller read `expected` - the CAS this feature's optimistic concurrency depends on.
        if (!_profiles.TryUpdate(key, updated, expected))
        {
            var latest = _profiles.TryGetValue(key, out var p) ? p : current;
            throw PersonProfileError.ConcurrencyConflict(expected.PersonId, expected.Type, expected.Version, latest.Version);
        }

        return Task.FromResult(updated);
    }
}
