namespace Biolog.Features.DeviceOperations;

/// <summary>BIO-DEV-004-AC04: thrown instead of attempting a health poll or remote job
/// against a vendor/model/firmware/sdk combination BIO-DEV-002's certification matrix says
/// is not supported - mirrors BIO-DEV-003's own
/// <c>UnsupportedDeviceCombinationException</c> exactly (same two "not usable" reasons:
/// an explicit <c>Unsupported</c> entry, or no matrix entry at all), translated at
/// <c>BioDev004Module</c>'s own gateway seam rather than referencing that type directly.</summary>
public sealed class DeviceCombinationUnsupportedException(string vendor, string model, string firmware, string sdk, string statusLabel, string? detail)
    : Exception($"{vendor}/{model}/{firmware}/{sdk} is not a supported combination ({statusLabel}).")
{
    public string StatusLabel { get; } = statusLabel;
    public string? Detail { get; } = detail;
}
