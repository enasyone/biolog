namespace Biolog.Api.Features.DeviceCertification;

/// <summary>Input to change an existing certification matrix entry's status/evidence
/// (BIO-DEV-002-AC04). Combines the target entry's key with the update in one request
/// because, unlike <see cref="Features.DeviceRegistry.Device"/>, a certification entry
/// has no single path-segment id - its identity is the vendor+model+firmware+sdk
/// combination itself. <see cref="ExpectedVersion"/> is required on every call
/// (ADR-0001 §3.2: a mutating write with no precondition is a failure, not a last-write-
/// wins fallback), mirroring <see cref="Features.DeviceRegistry.DeviceStatusChangeRequest"/>.</summary>
public sealed record DeviceCertificationStatusChangeRequest(
    string Vendor,
    string Model,
    string Firmware,
    string Sdk,
    CertificationStatus Status,
    string TestEvidenceReference,
    int ExpectedVersion,
    string ActorType,
    string ActorId,
    string? CorrelationId)
{
    public DeviceCertificationKey Key => new(Vendor, Model, Firmware, Sdk);
}
