using System.Text.Json.Serialization;

namespace Biolog.Api.Features.DeviceRegistry;

/// <summary>
/// A device's lifecycle status (BIO-DEV-001-AC01/AC03). Not a contracts-frozen enum - no
/// shared schema registers device status values yet - so this is this story's own,
/// deliberately small vocabulary rather than a guess at a future one: a device is
/// registered but not yet in service, in service, temporarily taken out of service, or
/// permanently retired.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<DeviceStatus>))]
public enum DeviceStatus
{
    PendingActivation,
    Active,
    Inactive,
    Retired,
}
