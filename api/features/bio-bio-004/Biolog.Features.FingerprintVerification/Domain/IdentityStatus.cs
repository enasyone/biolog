namespace Biolog.Features.FingerprintVerification.Domain;

/// <summary>
/// Mirrors BIO-IDN-001's own <c>Domain.PersonStatus</c> (Master Technical Specification §4)
/// exactly - the same deliberate, documented wire-string-level mirror BIO-IDN-004's own
/// <c>PersonStatus</c> uses for its <c>IPersonLifecycleGateway</c>. This project cannot
/// reference BIO-IDN-001's project at all (see this project's <c>.csproj</c> remarks), so this
/// type is never the source of truth for what a person's status actually is - only a value
/// <see cref="IIdentityStatusGateway"/>'s real implementation reports back, translated via
/// <see cref="IdentityStatusExtensions.ToWireString"/>/<see cref="IdentityStatusExtensions.TryParse"/>
/// rather than a shared enum type.
/// </summary>
public enum IdentityStatus
{
    PreEnrolled,
    Verified,
    Active,
    Suspended,
    Expired,
    FormerAlumni,
    Revoked,
    Archived,
}

public static class IdentityStatusExtensions
{
    public static string ToWireString(this IdentityStatus status) => status switch
    {
        IdentityStatus.PreEnrolled => "PRE_ENROLLED",
        IdentityStatus.Verified => "VERIFIED",
        IdentityStatus.Active => "ACTIVE",
        IdentityStatus.Suspended => "SUSPENDED",
        IdentityStatus.Expired => "EXPIRED",
        IdentityStatus.FormerAlumni => "FORMER_ALUMNI",
        IdentityStatus.Revoked => "REVOKED",
        IdentityStatus.Archived => "ARCHIVED",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "unreachable: exhaustive switch"),
    };

    public static bool TryParse(string? value, out IdentityStatus status)
    {
        switch (value)
        {
            case "PRE_ENROLLED": status = IdentityStatus.PreEnrolled; return true;
            case "VERIFIED": status = IdentityStatus.Verified; return true;
            case "ACTIVE": status = IdentityStatus.Active; return true;
            case "SUSPENDED": status = IdentityStatus.Suspended; return true;
            case "EXPIRED": status = IdentityStatus.Expired; return true;
            case "FORMER_ALUMNI": status = IdentityStatus.FormerAlumni; return true;
            case "REVOKED": status = IdentityStatus.Revoked; return true;
            case "ARCHIVED": status = IdentityStatus.Archived; return true;
            default: status = default; return false;
        }
    }
}
