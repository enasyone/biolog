using System.Text.Json.Serialization;

namespace Biolog.Features.DeviceAdapters;

/// <summary>BIO-DEV-003-AC02's device health vocabulary. <c>Unknown</c> is distinct from
/// <c>Offline</c>: the latter is an adapter that successfully reached the device kind's
/// simulated transport and got a "not reachable" answer; the former is reserved for a health
/// check that could not even be attempted (see <c>DeviceAdapterService</c> - it is never
/// produced by an adapter itself).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<DeviceHealthStatus>))]
public enum DeviceHealthStatus
{
    Online,
    Degraded,
    Offline,
    Unknown,
}

/// <summary>The result of a health check (BIO-DEV-003-AC02), never a vendor SDK type itself
/// (AC04). <see cref="Latency"/> is <c>null</c> whenever <see cref="Status"/> is
/// <see cref="DeviceHealthStatus.Offline"/> or <see cref="DeviceHealthStatus.Unknown"/> - there
/// is no round trip to time.</summary>
public sealed record DeviceHealth(
    DeviceHealthStatus Status,
    TimeSpan? Latency,
    string? Detail,
    DateTimeOffset CheckedAt);
