namespace Biolog.Features.DeviceAdapters;

/// <summary>BIO-DEV-003-AC03: thrown instead of attempting discovery/health against a
/// combination <see cref="ICertificationGate"/> says is not supported - carries the exact
/// certification status word so a caller (the HTTP module) can surface it, rather than a
/// generic failure.</summary>
public sealed class UnsupportedDeviceCombinationException(DeviceDescriptor descriptor, string statusLabel, string? detail)
    : Exception($"{descriptor.Vendor}/{descriptor.Model}/{descriptor.Firmware}/{descriptor.Sdk} is not a supported combination ({statusLabel}).")
{
    public DeviceDescriptor Descriptor { get; } = descriptor;
    public string StatusLabel { get; } = statusLabel;
    public string? Detail { get; } = detail;
}
