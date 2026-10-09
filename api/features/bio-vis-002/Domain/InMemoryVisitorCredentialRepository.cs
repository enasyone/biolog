using System.Collections.Concurrent;

namespace Biolog.Features.VisitorCredentials.Domain;

/// <summary>
/// A single, process-lifetime, cross-tenant dictionary standing in for the per-tenant database
/// (the same simplification every in-memory store in this codebase draws around itself). See
/// <see cref="IVisitorCredentialRepository"/> for why this class and its interface are
/// <c>internal</c>.
/// </summary>
internal sealed class InMemoryVisitorCredentialRepository : IVisitorCredentialRepository
{
    private readonly ConcurrentDictionary<string, VisitorCredential> _credentials = new(StringComparer.Ordinal);

    public Task<VisitorCredential> CreateAsync(VisitorCredential credential, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // BIO-VIS-002-F01's own remediation: a credential is now created Pending (not Granted) at
        // version 1 - see VisitorCredential.CreatePending's own header. Granted is only ever
        // reached via ReplaceAsync's own Pending -> Granted transition, once a real backing card
        // is genuinely Active.
        if (credential.Version != 1 || credential.Status != VisitorCredentialStatus.Pending)
        {
            throw new VisitorCredentialError(
                $"a new visitor credential must start Pending at version 1: {credential.Id}", "invalid_initial_state", 500);
        }

        if (!_credentials.TryAdd(credential.Id, credential))
        {
            // Unreachable in practice - ids are freshly minted UUIDv7s - but a collision on a
            // client-mintable identifier must fail loudly, not silently overwrite another
            // credential's record.
            throw new VisitorCredentialError(
                $"visitor credential id collision: {credential.Id}", "visitor_credential_id_collision", 500,
                new Dictionary<string, object?> { ["id"] = credential.Id });
        }

        return Task.FromResult(credential);
    }

    public Task<VisitorCredential?> GetAsync(string tenantId, string credentialId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_credentials.TryGetValue(credentialId, out var credential) || credential.TenantId != tenantId)
        {
            // Cross-tenant lookup returns null (caller maps to 404), never a distinct "exists
            // but forbidden" signal - ADR-0001 S1.5.
            return Task.FromResult<VisitorCredential?>(null);
        }

        return Task.FromResult<VisitorCredential?>(credential);
    }

    public Task<VisitorCredential?> GetActiveForVisitorAsync(string tenantId, string visitorRecordId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var active = _credentials.Values.FirstOrDefault(c =>
            c.TenantId == tenantId && c.VisitorRecordId == visitorRecordId && c.Status == VisitorCredentialStatus.Granted);
        return Task.FromResult(active);
    }

    public Task<IReadOnlyList<VisitorCredential>> ListForVisitorAsync(string tenantId, string visitorRecordId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<VisitorCredential> items = [.. _credentials.Values
            .Where(c => c.TenantId == tenantId && c.VisitorRecordId == visitorRecordId)
            .OrderBy(c => c.CreatedAt, StringComparer.Ordinal)
            .ThenBy(c => c.Id, StringComparer.Ordinal)];
        return Task.FromResult(items);
    }

    public Task<VisitorCredential?> FindByCardAsync(string tenantId, string personId, string cardClass, int sequenceNumber, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var owner = _credentials.Values.FirstOrDefault(c =>
            c.TenantId == tenantId && c.PersonId == personId && c.CardClass == cardClass && c.CardSequenceNumber == sequenceNumber);
        return Task.FromResult(owner);
    }

    public Task<VisitorCredential> ReplaceAsync(VisitorCredential expected, VisitorCredential updated, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_credentials.TryGetValue(expected.Id, out var current) || current.TenantId != expected.TenantId)
        {
            throw VisitorCredentialError.CredentialNotFound(expected.Id);
        }

        if (updated.Id != expected.Id || updated.TenantId != expected.TenantId)
        {
            throw new VisitorCredentialError(
                $"a visitor credential's id and tenant_id may never change once created: {expected.Id}", "identity_immutable", 500);
        }

        if (expected.Status == VisitorCredentialStatus.Revoked && updated.Status == VisitorCredentialStatus.Granted)
        {
            throw VisitorCredentialError.AlreadyRevoked(expected.Id);
        }

        // BIO-VIS-002-F01's own remediation: the only other allowed transitions. Anything else
        // (e.g. Granted -> Pending, Failed -> anything) is a bug in the caller, not a legitimate
        // state change - fail loudly rather than silently accept it.
        //
        // BIO-VIS-002-F04's own remediation (Seyi's round-3 FAIL): Pending -> Pending is also
        // allowed, but ONLY for GrantAsync's own early card-correlation attach (CardSequenceNumber
        // null -> a value, nothing else about the row changes) - see
        // VisitorCredentialService.AttachCardSequenceAsync's own header. This is a same-status
        // update specifically so a card's identity is durably correlated to this Pending row before
        // activation/audit/the final Granted-or-Failed transition, not a general-purpose "edit a
        // Pending row" escape hatch.
        var allowed = (expected.Status, updated.Status) switch
        {
            (VisitorCredentialStatus.Pending, VisitorCredentialStatus.Granted) => true,
            (VisitorCredentialStatus.Pending, VisitorCredentialStatus.Failed) => true,
            (VisitorCredentialStatus.Pending, VisitorCredentialStatus.Pending) => true,
            (VisitorCredentialStatus.Granted, VisitorCredentialStatus.Revoked) => true,
            _ => false,
        };
        if (!allowed)
        {
            throw VisitorCredentialError.InvalidTransition(expected.Id, expected.Status, updated.Status);
        }

        // Structural (record) equality: succeeds only if nothing has changed since the caller
        // read `expected` - the CAS this feature's optimistic concurrency depends on.
        if (!_credentials.TryUpdate(expected.Id, updated, expected))
        {
            var latest = _credentials.TryGetValue(expected.Id, out var p) ? p : current;
            throw VisitorCredentialError.ConcurrencyConflict(expected.Id, expected.Version, latest.Version);
        }

        return Task.FromResult(updated);
    }
}
