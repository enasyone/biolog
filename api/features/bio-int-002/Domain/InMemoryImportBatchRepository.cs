using System.Collections.Concurrent;

namespace Biolog.Features.Integrations.Domain;

/// <summary>A single, process-lifetime, cross-tenant dictionary standing in for the per-tenant database - same simplification every in-memory store in this codebase draws around itself.</summary>
internal sealed class InMemoryImportBatchRepository : IImportBatchRepository
{
    private readonly ConcurrentDictionary<string, ImportBatch> _batches = new(StringComparer.Ordinal);

    public Task<ImportBatch> CreateAsync(ImportBatch batch, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_batches.TryAdd(batch.Id, batch))
        {
            throw new ImportError($"import batch id collision: {batch.Id}", "import_batch_id_collision", 500,
                new Dictionary<string, object?> { ["id"] = batch.Id });
        }

        return Task.FromResult(batch);
    }

    public Task<ImportBatch?> GetAsync(string tenantId, string batchId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_batches.TryGetValue(batchId, out var batch) || batch.TenantId != tenantId)
        {
            return Task.FromResult<ImportBatch?>(null);
        }

        return Task.FromResult<ImportBatch?>(batch);
    }

    public Task<IReadOnlyList<ImportBatch>> ListByMappingAsync(string tenantId, string mappingId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<ImportBatch> items = [.. _batches.Values
            .Where(b => b.TenantId == tenantId && b.MappingId == mappingId)
            .OrderBy(b => b.SubmittedAt, StringComparer.Ordinal)
            .ThenBy(b => b.Id, StringComparer.Ordinal)];
        return Task.FromResult(items);
    }
}
