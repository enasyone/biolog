// Developer test fakes for BIO-RPT-004. SYNTHETIC fixtures only.
using Biolog.Features.OperationalDashboard.Domain;
using Biolog.Features.OperationalDashboard.Tenancy;

namespace Biolog.Features.OperationalDashboard.Tests;

public sealed class FakeCurrentTenantProvider(string? tenantId) : ICurrentTenantProvider
{
    public string? TenantId { get; set; } = tenantId;
}

/// <summary>Grants/denies by exact actor id (BIO-RPT-004-F01). Defaults to denying anything not explicitly granted - fail-closed, matching the real adapter's own fail-closed behaviour on a malformed/nonexistent actor.</summary>
public sealed class FakePermissionGateway : IDashboardPermissionGateway
{
    private readonly HashSet<string> _granted = new(StringComparer.Ordinal);

    public void Grant(string actorId) => _granted.Add(actorId);

    public Task<bool> HasPermissionAsync(string personId, string permissionKey, CancellationToken cancellationToken = default) =>
        Task.FromResult(_granted.Contains(personId));
}

/// <summary>A plain in-memory double - returns whatever <see cref="Seed"/> put in for that tenant, no gating. <see cref="ThrowOnNextCall"/> lets a test deterministically reproduce the "this one source is temporarily unavailable" path (BIO-RPT-004-F02) without any real I/O.</summary>
public sealed class FakeIdentityHealthGateway : IIdentityHealthGateway
{
    private readonly Dictionary<string, IdentityCounts> _byTenant = new(StringComparer.Ordinal);
    private bool _throwOnNextCall;

    /// <summary>BIO-RPT-004-F01: lets a test prove the permission gate runs strictly BEFORE this gateway is ever reached, not merely that a forbidden response happens to omit the data.</summary>
    public int CallCount { get; private set; }

    public void Seed(string tenantId, IdentityCounts counts) => _byTenant[tenantId] = counts;
    public void ThrowOnNextCall() => _throwOnNextCall = true;

    public Task<IdentityCounts> GetAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        CallCount++;
        if (_throwOnNextCall)
        {
            _throwOnNextCall = false;
            throw new InvalidOperationException("simulated identity gateway failure");
        }

        return Task.FromResult(_byTenant.TryGetValue(tenantId, out var counts) ? counts : new IdentityCounts(0, 0, 0, 0));
    }
}

public sealed class FakeAccessHealthGateway : IAccessHealthGateway
{
    private readonly Dictionary<string, AccessCounts> _byTenant = new(StringComparer.Ordinal);
    private bool _throwOnNextCall;

    public void Seed(string tenantId, AccessCounts counts) => _byTenant[tenantId] = counts;
    public void ThrowOnNextCall() => _throwOnNextCall = true;

    public Task<AccessCounts> GetAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        if (_throwOnNextCall)
        {
            _throwOnNextCall = false;
            throw new InvalidOperationException("simulated access gateway failure");
        }

        return Task.FromResult(_byTenant.TryGetValue(tenantId, out var counts) ? counts : new AccessCounts(0, 0, 0));
    }
}

public sealed class FakeProductionHealthGateway : IProductionHealthGateway
{
    private readonly Dictionary<string, ProductionCounts> _byTenant = new(StringComparer.Ordinal);
    private bool _throwOnNextCall;

    public void Seed(string tenantId, ProductionCounts counts) => _byTenant[tenantId] = counts;
    public void ThrowOnNextCall() => _throwOnNextCall = true;

    public Task<ProductionCounts> GetAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        if (_throwOnNextCall)
        {
            _throwOnNextCall = false;
            throw new InvalidOperationException("simulated production gateway failure");
        }

        return Task.FromResult(_byTenant.TryGetValue(tenantId, out var counts) ? counts : new ProductionCounts(0, 0, 0, 0, 0, 0));
    }
}
