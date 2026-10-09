using System.Collections.Concurrent;

namespace Biolog.Features.Integrations.Domain;

/// <summary>A single, process-lifetime, cross-tenant dictionary standing in for the per-tenant database - same simplification every in-memory store in this codebase draws around itself. Append/lookup only; no CAS needed, since a link is never mutated once created (only ever created once per (tenant, mapping, external key)).</summary>
internal sealed class InMemoryExternalIdentityLinkStore : IExternalIdentityLinkStore
{
    private readonly ConcurrentDictionary<(string TenantId, string MappingId, string ExternalKey), ExternalIdentityLink> _links = new();

    public Task<ExternalIdentityLink?> FindAsync(string tenantId, string mappingId, string externalKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_links.TryGetValue((tenantId, mappingId, externalKey), out var link) ? link : null);
    }

    public Task CreateAsync(ExternalIdentityLink link, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = (link.TenantId, link.MappingId, link.ExternalKey);
        if (!_links.TryAdd(key, link))
        {
            throw new ImportError(
                $"an external identity link already exists for mapping '{link.MappingId}' external key '{link.ExternalKey}'",
                "external_identity_link_already_exists", 500,
                new Dictionary<string, object?> { ["mapping_id"] = link.MappingId, ["external_key"] = link.ExternalKey });
        }

        return Task.CompletedTask;
    }
}
