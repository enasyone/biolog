namespace Biolog.Features.DeviceAdapters;

/// <summary>
/// The result of BIO-DEV-003-AC02's capability discovery: which <see cref="DeviceOperation"/>s
/// this exact vendor/model/firmware/sdk combination declares it supports, as reported by its
/// own adapter (<see cref="IDeviceAdapter.DiscoverCapabilitiesAsync"/>) - never a vendor SDK
/// type itself (AC04). <see cref="Operations"/> is deliberately a plain, closed list rather
/// than a per-operation detail object: this story's scope is "can it, yes/no", not the
/// operation-specific parameters (resolution, template format, ...) a later, operation-owning
/// story would need.
/// </summary>
public sealed record DeviceCapabilities(
    DeviceCategory Category,
    IReadOnlyList<DeviceOperation> Operations,
    DateTimeOffset DiscoveredAt);
