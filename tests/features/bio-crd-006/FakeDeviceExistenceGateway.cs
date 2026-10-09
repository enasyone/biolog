using Biolog.Features.CardLifecycle.Domain;
using Biolog.Features.CardLifecycle.Tenancy;

namespace Biolog.Features.CardLifecycle.Tests;

/// <summary>
/// Test double for <see cref="IDeviceExistenceGateway"/>. Unlike BIO-PHO-002's own fake (which
/// has no tenant concept to model), this one is deliberately tenant-aware: it mirrors the real
/// <c>DeviceExistenceGatewayAdapter</c>'s "IsActiveAsync only sees devices registered to the
/// AMBIENT tenant" behaviour (<see cref="IDeviceExistenceGateway"/>'s own header) by reading
/// the same <see cref="FakeCurrentTenantProvider"/> the test host's endpoints resolve through,
/// so a test can prove tenant binding - not just "a correct secret works" - without needing a
/// real host-channel test.
/// </summary>
public sealed class FakeDeviceExistenceGateway(
    ICurrentTenantProvider tenantProvider,
    IReadOnlyDictionary<string, string> deviceOwningTenant,
    IReadOnlySet<string>? inactiveDeviceIds = null) : IDeviceExistenceGateway
{
    public Task<bool> IsActiveAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        var isActive = deviceOwningTenant.TryGetValue(deviceId, out var owningTenant)
            && owningTenant == tenantProvider.TenantId
            && (inactiveDeviceIds is null || !inactiveDeviceIds.Contains(deviceId));
        return Task.FromResult(isActive);
    }
}
