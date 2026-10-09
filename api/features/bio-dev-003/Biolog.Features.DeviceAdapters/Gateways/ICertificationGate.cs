namespace Biolog.Features.DeviceAdapters;

/// <summary>
/// BIO-DEV-003-AC03: "Unsupported firmware/SDK combinations surface certification status."
/// This project cannot reference <c>Features.DeviceCertification.DeviceCertificationService</c>
/// directly (see the .csproj's own comment), so this narrow gateway is the seam - the same
/// "gateway, not a direct reference" shape BIO-MOB-003's own
/// <c>IDeviceTenantResolutionGateway</c> established. <c>BioDev003Module</c> supplies the real
/// implementation, translating a resolved <c>DeviceCertification</c>'s
/// <c>CertificationStatus</c> into <see cref="CertificationGateResult"/> below.
/// </summary>
public interface ICertificationGate
{
    Task<CertificationGateResult> CheckAsync(DeviceDescriptor descriptor, CancellationToken cancellationToken);
}

/// <summary>
/// <see cref="IsSupported"/> is <c>false</c> for both an explicit <c>Unsupported</c>
/// certification-matrix entry AND for a combination with no matrix entry at all - failing
/// closed on "never certified" exactly the way every other tenant-plane read/write in this
/// codebase fails closed on a missing resolution (ADR-0002 Decision 2's discipline, applied
/// here to a device combination instead of a tenant). <see cref="StatusLabel"/> carries the
/// real certification status word (<c>"Certified"</c>, <c>"Unsupported"</c>, ...) - or
/// <c>"NoCertificationRecord"</c> for the no-entry case, which is not itself one of
/// <c>CertificationStatus</c>'s values, so callers can tell the two "not usable" reasons
/// apart without this project needing to reference that enum.
/// </summary>
public sealed record CertificationGateResult(bool IsSupported, string StatusLabel, string? Detail);
