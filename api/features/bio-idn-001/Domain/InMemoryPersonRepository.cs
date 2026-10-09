using System.Collections.Concurrent;

namespace Biolog.Features.PersonRegistry.Domain;

/// <summary>
/// A single, process-lifetime, cross-tenant dictionary standing in for the per-tenant
/// database ADR-0002 Decision 4.1 describes - real per-tenant routing is a later story's
/// concern, the same simplification <c>InMemoryDeviceRepository</c> (BIO-DEV-001) already
/// draws around itself. See <see cref="IPersonRepository"/> for why this class and its
/// interface are <c>internal</c> (BIO-IDN-001-QA-01).
///
/// <b>Belt-and-braces validation, independent of <see cref="PersonService"/>.</b>
/// <see cref="CreateAsync"/> requires a freshly-constructed aggregate's initial state
/// (<see cref="PersonStatus.PreEnrolled"/>, version 1); <see cref="ReplaceAsync"/> requires
/// <paramref name="updated"/>'s identity and tenant to match <paramref name="expected"/>'s
/// exactly, and the status transition between them to be legal per
/// <see cref="PersonLifecycleTransitions"/> - the SAME table <see cref="PersonService"/>
/// already checks before ever calling this method. This is deliberately redundant with
/// <see cref="Person"/>'s own get-only <see cref="Person.Id"/>/<see cref="Person.TenantId"/>
/// (which already make a <c>current with { ... }</c>-derived <paramref name="updated"/>
/// structurally incapable of naming a different id/tenant): the checks here also catch two
/// unrelated <see cref="Person"/> instances being passed together, not only a mutated copy of
/// one - the same "validated at the request boundary AND the repository boundary" discipline
/// <c>CredentialReferenceValidator</c> (BIO-DEV-001-QA-04) established.
/// </summary>
internal sealed class InMemoryPersonRepository : IPersonRepository, ICrossTenantPersonRevocationLookup
{
    private readonly ConcurrentDictionary<string, Person> _people = new(StringComparer.Ordinal);

    public Task<Person> CreateAsync(Person person, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (person.Status != PersonStatus.PreEnrolled || person.Version != 1)
        {
            throw PersonError.InvalidInitialState(person.Id, person.Status, person.Version);
        }

        if (!_people.TryAdd(person.Id, person))
        {
            // Unreachable in practice - ids are freshly minted UUIDv7s - but a collision on
            // a client-mintable identifier (ADR-0001 §1.3) must fail loudly, not silently
            // overwrite another person's record.
            throw new PersonError(
                $"person id collision: {person.Id}", "person_id_collision", 500,
                new Dictionary<string, object?> { ["id"] = person.Id });
        }

        return Task.FromResult(person);
    }

    public Task<Person?> GetAsync(string tenantId, string personId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_people.TryGetValue(personId, out var person) || person.TenantId != tenantId)
        {
            // Cross-tenant lookup returns null (the caller maps this to 404), never a
            // distinct "exists but forbidden" signal - ADR-0001 §1.5: "a canonical
            // identifier ... carries no authority ... must fail closed ... as not found,
            // never as forbidden, so identifiers cannot be probed for existence across
            // tenants."
            return Task.FromResult<Person?>(null);
        }

        return Task.FromResult<Person?>(person);
    }

    public Task<IReadOnlyList<Person>> ListByTenantAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<Person> items = [.. _people.Values.Where(p => p.TenantId == tenantId).OrderBy(p => p.Id, StringComparer.Ordinal)];
        return Task.FromResult(items);
    }

    public Task<Person> ReplaceAsync(Person expected, Person updated, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_people.TryGetValue(expected.Id, out var current) || current.TenantId != expected.TenantId)
        {
            throw PersonError.NotFound(expected.Id);
        }

        if (updated.Id != expected.Id || updated.TenantId != expected.TenantId)
        {
            // BIO-IDN-001-QA-01: identity and tenant may never move via a replace - see class
            // header. Checked before the transition/CAS below since a candidate that fails
            // this is invalid regardless of what else about it looks plausible.
            throw PersonError.IdentityOrTenantImmutable(expected.Id);
        }

        if (!PersonLifecycleTransitions.IsAllowed(expected.Status, updated.Status))
        {
            // BIO-IDN-001-QA-01: the SAME transition table PersonService already checked -
            // PersonService never reaches this method with an illegal or no-op transition
            // (it returns early on a no-op before calling ReplaceAsync), so this is
            // unreachable via any legitimate call, and exists purely as the repository-
            // boundary half of the dual-boundary discipline.
            throw PersonError.InvalidStatusTransition(expected.Status, updated.Status);
        }

        // Structural (record) equality: succeeds only if nothing has changed since the
        // caller read `expected` - the CAS this feature's optimistic concurrency depends on.
        if (!_people.TryUpdate(expected.Id, updated, expected))
        {
            var latest = _people.TryGetValue(expected.Id, out var p) ? p : current;
            throw PersonError.ConcurrencyConflict(expected.Id, expected.Version, latest.Version);
        }

        return Task.FromResult(updated);
    }

    /// <summary><see cref="ICrossTenantPersonRevocationLookup"/> - BIO-ACC-003's own device-authenticated exception to the ambient-tenant rule every other method here enforces (see that interface's header). Filters the same underlying store <see cref="ListByTenantAsync"/> reads, never a second copy.</summary>
    public Task<IReadOnlyList<Person>> ListRevokedSinceAsync(string tenantId, string? since, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IEnumerable<Person> revoked = _people.Values.Where(p => p.TenantId == tenantId && p.Status == PersonStatus.Revoked);
        if (since is not null)
        {
            revoked = revoked.Where(p => string.CompareOrdinal(p.UpdatedAt, since) >= 0);
        }

        IReadOnlyList<Person> items = [.. revoked.OrderBy(p => p.UpdatedAt, StringComparer.Ordinal).ThenBy(p => p.Id, StringComparer.Ordinal)];
        return Task.FromResult(items);
    }
}
