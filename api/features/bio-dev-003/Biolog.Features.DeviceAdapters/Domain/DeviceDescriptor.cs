namespace Biolog.Features.DeviceAdapters;

/// <summary>
/// This project's own, minimal copy of "which real device" - Vendor/Model/Firmware/Sdk,
/// field-for-field matching <c>Features.DeviceRegistry.Device</c>'s and
/// <c>Features.DeviceCertification.DeviceCertificationKey</c>'s own field names in
/// Biolog.Api, so a caller on that side can build one from a real <c>Device</c> with a
/// straight field copy - but this project cannot reference either of those types directly
/// (see the .csproj's own comment for why), so this is a deliberate, narrow duplication of
/// shape, not a shared type. <see cref="Serial"/> is optional: capability discovery and
/// health (AC02) key off Vendor+Model+Firmware+Sdk exactly like the certification matrix
/// does, not off one physical unit's own serial number - Serial is carried only so an
/// adapter's health check can report which specific unit it polled.
/// </summary>
public sealed record DeviceDescriptor(string Vendor, string Model, string Firmware, string Sdk, string? Serial = null);
