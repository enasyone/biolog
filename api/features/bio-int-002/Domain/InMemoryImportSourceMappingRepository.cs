using System.Collections.Concurrent;

namespace Biolog.Features.Integrations.Domain;

/// <summary>A single, process-lifetime, cross-tenant dictionary standing in for the per-tenant database (the same simplification every in-memory store in this codebase draws around itself).</summary>
internal sealed class InMemoryImportSourceMappingRepository : IImportSourceMappingRepository
{
    private readonly ConcurrentDictionary<string, ImportSourceMapping> _mappings = new(StringComparer.Ordinal);

    public Task<ImportSourceMapping> CreateAsync(ImportSourceMapping mapping, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (mapping.Version != 1)
        {
            throw new ImportError($"a new import source mapping must start at version 1: {mapping.Id}", "invalid_initial_state", 500);
        }

        if (!_mappings.TryAdd(mapping.Id, mapping))
        {
            throw new ImportError($"import source mapping id collision: {mapping.Id}", "import_mapping_id_collision", 500,
                new Dictionary<string, object?> { ["id"] = mapping.Id });
        }

        return Task.FromResult(mapping);
    }

    public Task<ImportSourceMapping?> GetAsync(string tenantId, string mappingId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_mappings.TryGetValue(mappingId, out var mapping) || mapping.TenantId != tenantId)
        {
            // Cross-tenant lookup returns null (caller maps to 404), never a distinct "exists
            // but forbidden" signal - ADR-0001 S1.5.
            return Task.FromResult<ImportSourceMapping?>(null);
        }

        return Task.FromResult<ImportSourceMapping?>(mapping);
    }

    public Task<IReadOnlyList<ImportSourceMapping>> ListByTenantAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<ImportSourceMapping> items = [.. _mappings.Values
            .Where(m => m.TenantId == tenantId)
            .OrderBy(m => m.CreatedAt, StringComparer.Ordinal)
            .ThenBy(m => m.Id, StringComparer.Ordinal)];
        return Task.FromResult(items);
    }

    public Task<ImportSourceMapping> ReplaceAsync(ImportSourceMapping expected, ImportSourceMapping updated, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_mappings.TryGetValue(expected.Id, out var current) || current.TenantId != expected.TenantId)
        {
            throw ImportError.MappingNotFound(expected.Id);
        }

        if (updated.Id != expected.Id || updated.TenantId != expected.TenantId)
        {
            throw new ImportError($"an import source mapping's id and tenant_id may never change once created: {expected.Id}", "identity_immutable", 500);
        }

        // Structural (record) equality: succeeds only if nothing has changed since the caller
        // read `expected` - the CAS this feature's optimistic concurrency depends on.
        if (!_mappings.TryUpdate(expected.Id, updated, expected))
        {
            var latest = _mappings.TryGetValue(expected.Id, out var p) ? p : current;
            throw ImportError.ConcurrencyConflict(expected.Id, expected.Version, latest.Version);
        }

        return Task.FromResult(updated);
    }
}
