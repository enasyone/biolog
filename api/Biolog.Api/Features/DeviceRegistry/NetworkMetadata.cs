namespace Biolog.Api.Features.DeviceRegistry;

/// <summary>Network metadata for a device (BIO-DEV-001-AC01). All fields optional - a
/// newly-registered device may not have connected yet, and a serial-only reader may never
/// carry a MAC address at all.</summary>
public sealed record NetworkMetadata(string? IpAddress, string? MacAddress, string? Hostname);
