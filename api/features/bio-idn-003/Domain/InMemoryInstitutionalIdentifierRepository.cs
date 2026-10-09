using System.Collections.Concurrent;

namespace Biolog.Features.InstitutionalIdentifiers.Domain;

/// <summary>
/// A single, process-lifetime, cross-tenant dictionary standing in for the per-tenant
/// database ADR-0002 Decision 4.1 describes, keyed by
/// <c>(PersonId, Type, CustomLabel, SequenceNumber)</c> - the same simplification every sibling
/// in-memory repository already draws around itself. See
/// <see cref="IInstitutionalIdentifierRepository"/> for why this class is <c>internal</c> and
/// what belt-and-braces validation it applies independently of
/// <see cref="InstitutionalIdentifierService"/>.
/// </summary>
internal sealed class InMemoryInstitutionalIdentifierRepository : IInstitutionalIdentifierRepository
{
    private readonly ConcurrentDictionary<(string PersonId, IdentifierType Type, string? CustomLabel, int SequenceNumber), InstitutionalIdentifier> _identifiers = new();

    public Task<InstitutionalIdentifier> CreateAsync(InstitutionalIdentifier identifier, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (identifier.Status != IdentifierStatus.Active || identifier.Version != 1)
        {
            throw InstitutionalIdentifierError.InvalidInitialState(identifier.PersonId, identifier.Type, identifier.SequenceNumber, identifier.Status, identifier.Version);
        }

        // AC03's real structural enforcement point: at most one ACTIVE record per
        // (person, type, custom_label) at any time - independently re-checked here, not
        // merely trusted from InstitutionalIdentifierService's own pre-lock check.
        var hasActive = _identifiers.Values.Any(i =>
            i.TenantId == identifier.TenantId && i.PersonId == identifier.PersonId && i.Type == identifier.Type && i.CustomLabel == identifier.CustomLabel
            && i.Status == IdentifierStatus.Active);
        if (hasActive)
        {
            throw InstitutionalIdentifierError.IdentifierAlreadyActive(identifier.PersonId, identifier.Type, identifier.CustomLabel);
        }

        var key = (identifier.PersonId, identifier.Type, identifier.CustomLabel, identifier.SequenceNumber);
        if (!_identifiers.TryAdd(key, identifier))
        {
            // A genuine natural-key collision (e.g. a mis-computed sequence number) - never
            // legitimately reachable via InstitutionalIdentifierService, which always computes
            // SequenceNumber from this same repository's own history under the per-key lock.
            throw InstitutionalIdentifierError.InvalidInitialState(identifier.PersonId, identifier.Type, identifier.SequenceNumber, identifier.Status, identifier.Version);
        }

        return Task.FromResult(identifier);
    }

    public Task<InstitutionalIdentifier?> GetActiveAsync(
        string tenantId, string personId, IdentifierType type, string? customLabel, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var match = _identifiers.Values.FirstOrDefault(i =>
            i.TenantId == tenantId && i.PersonId == personId && i.Type == type && i.CustomLabel == customLabel && i.Status == IdentifierStatus.Active);
        return Task.FromResult(match);
    }

    public Task<InstitutionalIdentifier?> GetAsync(
        string tenantId, string personId, IdentifierType type, string? customLabel, int sequenceNumber, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_identifiers.TryGetValue((personId, type, customLabel, sequenceNumber), out var identifier) || identifier.TenantId != tenantId)
        {
            // Cross-tenant lookup returns null (mapped to 404 by the caller), never a
            // distinct "exists but forbidden" signal - ADR-0001 §1.5.
            return Task.FromResult<InstitutionalIdentifier?>(null);
        }

        return Task.FromResult<InstitutionalIdentifier?>(identifier);
    }

    public Task<IReadOnlyList<InstitutionalIdentifier>> ListHistoryAsync(
        string tenantId, string personId, IdentifierType type, string? customLabel, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<InstitutionalIdentifier> items =
        [
            .. _identifiers.Values
                .Where(i => i.TenantId == tenantId && i.PersonId == personId && i.Type == type && i.CustomLabel == customLabel)
                .OrderBy(i => i.SequenceNumber),
        ];
        return Task.FromResult(items);
    }

    public Task<IReadOnlyList<InstitutionalIdentifier>> ListByPersonAsync(string tenantId, string personId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<InstitutionalIdentifier> items =
        [
            .. _identifiers.Values
                .Where(i => i.TenantId == tenantId && i.PersonId == personId)
                .OrderBy(i => i.Type.ToWireString(), StringComparer.Ordinal)
                .ThenBy(i => i.CustomLabel, StringComparer.Ordinal)
                .ThenBy(i => i.SequenceNumber),
        ];
        return Task.FromResult(items);
    }

    public Task<bool> ValueExistsAsync(
        string tenantId, IdentifierType type, string? customLabel, string value, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var exists = _identifiers.Values.Any(i =>
            i.TenantId == tenantId && i.Type == type && i.CustomLabel == customLabel && string.Equals(i.Value, value, StringComparison.Ordinal));
        return Task.FromResult(exists);
    }

    public Task<bool> HasDuplicateValuesAsync(
        string tenantId, IdentifierType type, string? customLabel, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var hasDuplicate = _identifiers.Values
            .Where(i => i.TenantId == tenantId && i.Type == type && i.CustomLabel == customLabel)
            .GroupBy(i => i.Value, StringComparer.Ordinal)
            .Any(g => g.Count() > 1);
        return Task.FromResult(hasDuplicate);
    }

    public Task<InstitutionalIdentifier> ReplaceAsync(InstitutionalIdentifier expected, InstitutionalIdentifier updated, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = (expected.PersonId, expected.Type, expected.CustomLabel, expected.SequenceNumber);
        if (!_identifiers.TryGetValue(key, out var current) || current.TenantId != expected.TenantId)
        {
            throw InstitutionalIdentifierError.IdentifierNotFound(expected.PersonId, expected.Type, expected.CustomLabel, expected.SequenceNumber);
        }

        if (updated.PersonId != expected.PersonId || updated.TenantId != expected.TenantId || updated.Type != expected.Type
            || updated.CustomLabel != expected.CustomLabel || updated.SequenceNumber != expected.SequenceNumber || updated.Value != expected.Value)
        {
            // Identity (including Value - see InstitutionalIdentifier's header) may never move
            // via a replace. Unreachable via `expected with { ... }` (these fields are
            // get-only), so this catches two unrelated instances being passed together.
            throw InstitutionalIdentifierError.IdentityImmutable(expected.PersonId, expected.Type, expected.SequenceNumber);
        }

        if (expected.Status == IdentifierStatus.Retired)
        {
            // The only governed transition is Active -> Retired, and it is terminal -
            // InstitutionalIdentifierService.RetireAsync returns early (no-op) on an
            // already-retired record before ever calling this method, so this is unreachable
            // via any legitimate call and exists purely as the repository-boundary half of the
            // dual-boundary discipline.
            throw InstitutionalIdentifierError.AlreadyRetired(expected.PersonId, expected.Type, expected.SequenceNumber);
        }

        // Structural (record) equality: succeeds only if nothing has changed since the
        // caller read `expected` - the CAS this feature's optimistic concurrency depends on.
        if (!_identifiers.TryUpdate(key, updated, expected))
        {
            var latest = _identifiers.TryGetValue(key, out var i) ? i : current;
            throw InstitutionalIdentifierError.ConcurrencyConflict(expected.PersonId, expected.Type, expected.SequenceNumber, expected.Version, latest.Version);
        }

        return Task.FromResult(updated);
    }
}
