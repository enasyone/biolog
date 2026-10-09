namespace Biolog.Features.DeviceOperations;

/// <summary>Read-only seam onto a real, tenant-resolved <c>Features.DeviceRegistry.Device</c>
/// (BIO-DEV-001) - <c>BioDev004Module</c> supplies the real implementation. <c>null</c> from
/// <see cref="GetAsync"/> means not-found-or-cross-tenant, the same indistinguishable shape
/// <c>IDeviceRepository.GetAsync</c> already established.</summary>
public interface IDeviceDirectoryGateway
{
    Task<DeviceSnapshot?> GetAsync(string deviceId, CancellationToken cancellationToken);

    Task<IReadOnlyList<DeviceSnapshot>> ListAsync(CancellationToken cancellationToken);
}
