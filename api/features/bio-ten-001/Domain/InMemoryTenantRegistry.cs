using System.Collections.Concurrent;

namespace Biolog.Features.TenantRegistry.Domain;

/// <summary>In-process store for BIO-TEN-001. See <see cref="ITenantRegistry"/> for why.</summary>
public sealed class InMemoryTenantRegistry : ITenantRegistry
{
    private readonly ConcurrentDictionary<string, Tenant> _tenants = new(StringComparer.Ordinal);

    public Task<Tenant> CreateAsync(Tenant tenant, CancellationToken cancellationToken = default)
    {
        if (!_tenants.TryAdd(tenant.Id, tenant))
        {
            // Unreachable in practice - ids are freshly minted UUIDv7s - but a collision on
            // a client-mintable identifier (ADR-0001 §1.3) must fail loudly, not silently
            // overwrite another tenant's record.
            throw new TenantRegistryError(
                $"tenant id collision: {tenant.Id}", "tenant_id_collision", 500,
                new Dictionary<string, object?> { ["id"] = tenant.Id });
        }

        return Task.FromResult(tenant);
    }

    public Task<Tenant?> GetAsync(string id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_tenants.TryGetValue(id, out var tenant) ? tenant : null);

    public Task<(IReadOnlyList<Tenant> Items, bool HasMore)> ListAsync(
        int limit, string? afterId, TenantStatus? statusFilter, CancellationToken cancellationToken = default)
    {
        var ordered = _tenants.Values
            .Where(t => statusFilter is null || t.Status == statusFilter)
            .OrderBy(t => t.Id, StringComparer.Ordinal);

        if (afterId is not null)
        {
            ordered = ordered.Where(t => string.CompareOrdinal(t.Id, afterId) > 0).OrderBy(t => t.Id, StringComparer.Ordinal);
        }

        var page = ordered.Take(limit + 1).ToList();
        var hasMore = page.Count > limit;
        var items = hasMore ? page.Take(limit).ToList() : page;
        return Task.FromResult<(IReadOnlyList<Tenant>, bool)>((items, hasMore));
    }

    public Task<Tenant> ReplaceAsync(Tenant expected, Tenant updated, CancellationToken cancellationToken = default)
    {
        if (!_tenants.TryGetValue(expected.Id, out var current))
        {
            throw TenantRegistryError.NotFound(expected.Id);
        }

        // Structural (record) equality: succeeds only if nothing has changed since the
        // caller read `expected` - the CAS this feature's optimistic concurrency depends on.
        if (!_tenants.TryUpdate(expected.Id, updated, expected))
        {
            var latest = _tenants.TryGetValue(expected.Id, out var t) ? t : current;
            throw TenantRegistryError.ConcurrencyConflict(expected.Id, expected.Version, latest.Version);
        }

        return Task.FromResult(updated);
    }
}
