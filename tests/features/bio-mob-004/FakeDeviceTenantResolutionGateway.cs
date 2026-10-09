using Biolog.Features.MobileVerification.Domain;

namespace Biolog.Features.MobileVerification.Tests;

/// <summary>Test double for <see cref="IDeviceTenantResolutionGateway"/>. Resolves any registered device id to its configured tenant; an unregistered id resolves to null (mirrors an inactive/unknown device).</summary>
public sealed class FakeDeviceTenantResolutionGateway : IDeviceTenantResolutionGateway
{
    private readonly Dictionary<string, string> _tenantByDeviceId = [];

    public FakeDeviceTenantResolutionGateway Register(string deviceId, string tenantId)
    {
        _tenantByDeviceId[deviceId] = tenantId;
        return this;
    }

    public Task<ResolvedDevice?> ResolveAsync(string deviceId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_tenantByDeviceId.TryGetValue(deviceId, out var tenantId) ? new ResolvedDevice(deviceId, tenantId) : null);
}
