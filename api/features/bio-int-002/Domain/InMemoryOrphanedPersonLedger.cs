using System.Collections.Concurrent;

namespace Biolog.Features.Integrations.Domain;

/// <summary>See <see cref="IOrphanedPersonLedger"/> for why this exists. A single, process-lifetime, cross-tenant dictionary - the same simplification every in-memory store in this codebase draws around itself.</summary>
internal sealed class InMemoryOrphanedPersonLedger : IOrphanedPersonLedger
{
    private readonly ConcurrentDictionary<(string TenantId, string MappingId, string ExternalKey), string> _personIdsByKey = new();

    public Task RecordAsync(string tenantId, string mappingId, string externalKey, string personId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _personIdsByKey[(tenantId, mappingId, externalKey)] = personId;
        return Task.CompletedTask;
    }

    public Task<string?> FindAsync(string tenantId, string mappingId, string externalKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_personIdsByKey.TryGetValue((tenantId, mappingId, externalKey), out var personId) ? personId : null);
    }
}
