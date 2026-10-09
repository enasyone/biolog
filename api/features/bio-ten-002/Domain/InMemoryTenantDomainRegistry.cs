using System.Collections.Concurrent;

namespace Biolog.Features.TenantDomainRegistry.Domain;

/// <summary>
/// In-process store for BIO-TEN-002. Process-lifetime only, not durable across a restart -
/// see <see cref="TenantDomainService"/>'s class header for what that does and does not mean
/// for this story's atomicity guarantees (ADR-0003 Decision 7).
/// </summary>
public sealed class InMemoryTenantDomainRegistry : ITenantDomainRegistry
{
    private readonly ConcurrentDictionary<string, TenantDomain> _domains = new(StringComparer.Ordinal);

    public Task<TenantDomain> CreateAsync(TenantDomain domain, CancellationToken cancellationToken = default)
    {
        if (!_domains.TryAdd(domain.Hostname, domain))
        {
            throw TenantDomainError.DuplicateHostname(domain.Hostname);
        }

        return Task.FromResult(domain);
    }

    public Task<TenantDomain?> GetByHostnameAsync(string hostname, CancellationToken cancellationToken = default) =>
        Task.FromResult(_domains.GetValueOrDefault(hostname));

    public Task<IReadOnlyList<TenantDomain>> ListByTenantAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<TenantDomain> result = [.. _domains.Values.Where(d => d.TenantId == tenantId)];
        return Task.FromResult(result);
    }

    public Task<TenantDomain> ReplaceAsync(TenantDomain expected, TenantDomain updated, CancellationToken cancellationToken = default)
    {
        if (!_domains.TryUpdate(expected.Hostname, updated, expected))
        {
            throw TenantDomainError.ConcurrencyConflict(expected.Hostname, expected.Version, GetActualVersion(expected.Hostname));
        }

        return Task.FromResult(updated);
    }

    private int GetActualVersion(string hostname) => _domains.TryGetValue(hostname, out var current) ? current.Version : -1;
}
