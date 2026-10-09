namespace Biolog.Features.DeviceOperations;

/// <summary>No device with this id is visible to the resolved tenant - the same
/// not-found-not-forbidden shape <c>Features.DeviceRegistry.IDeviceRepository.GetAsync</c>
/// already established (a cross-tenant device answers identically to an unknown one).</summary>
public sealed class DeviceNotFoundException(string deviceId) : Exception($"No device '{deviceId}' is visible to the resolved tenant.")
{
    public string DeviceId { get; } = deviceId;
}
