using System.Text.Json.Serialization;

namespace Biolog.Features.DeviceOperations;

/// <summary>
/// This project's own copy of BIO-DEV-003's <c>DeviceHealthStatus</c> vocabulary
/// (Online/Degraded/Offline/Unknown) - field-for-field identical, translated at the one
/// seam that can see both projects (<c>BioDev004Module</c>'s
/// <c>DeviceHealthCheckGatewayAdapter</c>), the same "deliberate narrow duplication of
/// shape, not a shared type" pattern BIO-DEV-003's own <c>DeviceDescriptor</c> already
/// established for <c>Features.DeviceRegistry.Device</c>.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<DeviceHealthStatus>))]
public enum DeviceHealthStatus
{
    Online,
    Degraded,
    Offline,
    Unknown,
}
