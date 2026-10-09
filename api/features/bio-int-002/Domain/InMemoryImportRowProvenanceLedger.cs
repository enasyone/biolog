using System.Collections.Concurrent;

namespace Biolog.Features.Integrations.Domain;

/// <summary>See <see cref="IImportRowProvenanceLedger"/> for why this exists. A single, process-lifetime, cross-tenant dictionary - the same simplification every in-memory store in this codebase draws around itself.</summary>
internal sealed class InMemoryImportRowProvenanceLedger : IImportRowProvenanceLedger
{
    private readonly ConcurrentDictionary<(string TenantId, string IdempotencyKey, string MappingId, string ExternalKey), ImportRowProvenanceEntry> _entries = new();

    public Task RecordAsync(string tenantId, string idempotencyKey, string mappingId, string externalKey, ImportRowOutcome outcome, string fingerprint, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _entries[(tenantId, idempotencyKey, mappingId, externalKey)] = new ImportRowProvenanceEntry(outcome, fingerprint);
        return Task.CompletedTask;
    }

    public Task<ImportRowProvenanceEntry?> FindAsync(string tenantId, string idempotencyKey, string mappingId, string externalKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_entries.TryGetValue((tenantId, idempotencyKey, mappingId, externalKey), out var entry) ? entry : null);
    }
}
