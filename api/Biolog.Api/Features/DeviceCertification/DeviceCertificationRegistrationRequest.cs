using System.Text.Json.Serialization;

namespace Biolog.Api.Features.DeviceCertification;

/// <summary>Input to register a new certification matrix entry (BIO-DEV-002-AC01). No
/// <c>Version</c> - assigned by the service, starting at 1. Whether this becomes the
/// platform-wide default or one tenant's own override (BIO-DEV-002-AC03) is decided by
/// which <see cref="DeviceCertificationService"/> method (and, at the HTTP layer, which
/// endpoint group) receives it, not by a field on this request.
/// <see cref="ActorType"/>/<see cref="ActorId"/> are the caller's self-reported identity -
/// no auth story has shipped a verified session to derive them from yet, the same
/// documented gap as <see cref="Features.DeviceRegistry.DeviceStatusChangeRequest"/>.</summary>
public sealed record DeviceCertificationRegistrationRequest(
    string Vendor,
    string Model,
    string Firmware,
    string Sdk,
    CertificationStatus Status,
    string TestEvidenceReference,
    string ActorType,
    string ActorId,
    string? CorrelationId)
{
    public DeviceCertificationKey Key => new(Vendor, Model, Firmware, Sdk);
}
