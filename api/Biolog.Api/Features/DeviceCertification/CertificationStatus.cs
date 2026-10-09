using System.Text.Json.Serialization;

namespace Biolog.Api.Features.DeviceCertification;

/// <summary>
/// A device+SDK+firmware combination's certification status (BIO-DEV-002-AC01/AC02), the
/// five values the user story names verbatim: fully certified by the platform, supported
/// without full certification, experimental (works today, not yet vetted), deprecated
/// (was supported, being phased out) and unsupported (known not to work, or never
/// evaluated - both surface identically to admins, see AC02). Not a contracts-frozen
/// enum, the same "this story's own small vocabulary" note
/// <see cref="Features.DeviceRegistry.DeviceStatus"/> already carries - no shared schema
/// registers certification status values yet.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<CertificationStatus>))]
public enum CertificationStatus
{
    Certified,
    Supported,
    Experimental,
    Deprecated,
    Unsupported,
}
