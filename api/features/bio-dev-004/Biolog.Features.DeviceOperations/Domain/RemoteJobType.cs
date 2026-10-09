using System.Text.Json.Serialization;

namespace Biolog.Features.DeviceOperations;

/// <summary>
/// BIO-DEV-004-AC02: the closed set of "controlled configuration jobs" the user story
/// names - deliberately small and explicit rather than an open string, the same choice
/// BIO-DEV-003's own <c>DeviceCategory</c>/<c>DeviceOperation</c> made for an identical
/// reason (no shared schema registers remote device job types yet).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<RemoteJobType>))]
public enum RemoteJobType
{
    Reboot,
    PushConfiguration,
    UpdateFirmware,
}
