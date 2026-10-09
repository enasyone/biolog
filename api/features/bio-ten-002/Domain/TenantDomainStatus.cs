namespace Biolog.Features.TenantDomainRegistry.Domain;

/// <summary>
/// BIO-TEN-002-AC01/AC03. Matches <c>contracts/schemas/tenant-domain.schema.json</c>'s
/// <c>status</c> enum exactly. Only <see cref="Verified"/> resolves a host to a tenant
/// (ADR-0002 §1.3); every other value fails closed identically from the resolver's point of
/// view (<see cref="Domain.TenantDomainError.HostNotRecognised"/>).
/// </summary>
public enum TenantDomainStatus
{
    PendingVerification,
    Verified,
    Suspended,
    Retired,
}

public static class TenantDomainStatusExtensions
{
    public static string ToWireString(this TenantDomainStatus status) => status switch
    {
        TenantDomainStatus.PendingVerification => "PENDING_VERIFICATION",
        TenantDomainStatus.Verified => "VERIFIED",
        TenantDomainStatus.Suspended => "SUSPENDED",
        TenantDomainStatus.Retired => "RETIRED",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "unreachable: exhaustive switch"),
    };

    public static bool TryParse(string? value, out TenantDomainStatus status)
    {
        switch (value)
        {
            case "PENDING_VERIFICATION": status = TenantDomainStatus.PendingVerification; return true;
            case "VERIFIED": status = TenantDomainStatus.Verified; return true;
            case "SUSPENDED": status = TenantDomainStatus.Suspended; return true;
            case "RETIRED": status = TenantDomainStatus.Retired; return true;
            default: status = default; return false;
        }
    }
}
