using Biolog.Features.PhotoBooth.Domain;

namespace Biolog.Features.PhotoBooth.Tests;

/// <summary>Test double for <see cref="IDeviceExistenceGateway"/>. Defaults to "every device is active".</summary>
public sealed class FakeDeviceExistenceGateway(IReadOnlySet<string>? activeDeviceIds = null) : IDeviceExistenceGateway
{
    public Task<bool> IsActiveAsync(string deviceId, CancellationToken cancellationToken = default) =>
        Task.FromResult(activeDeviceIds is null || activeDeviceIds.Contains(deviceId));
}
