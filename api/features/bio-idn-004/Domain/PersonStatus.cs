namespace Biolog.Features.PersonLifecycle.Domain;

/// <summary>
/// Mirrors BIO-IDN-001's own <c>Domain.PersonStatus</c> (Master Technical Specification §4)
/// exactly - the 8 states are canonical there, not owned by this story. This project cannot
/// reference BIO-IDN-001's project at all (no <c>ProjectReference</c>, see this project's
/// <c>.csproj</c> remarks), so this is a deliberate, documented, wire-string-level mirror used
/// only to talk about a status BIO-IDN-001's own <see cref="IPersonLifecycleGateway"/>
/// implementation reports back - it is never the source of truth for what a person's status
/// actually is, and never independently persisted here.
/// </summary>
public enum PersonStatus
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

public static class PersonStatusExtensions
{
    public static string ToWireString(this PersonStatus status) => status switch
    {
        PersonStatus.PreEnrolled => "PRE_ENROLLED",
        PersonStatus.Verified => "VERIFIED",
        PersonStatus.Active => "ACTIVE",
        PersonStatus.Suspended => "SUSPENDED",
        PersonStatus.Expired => "EXPIRED",
        PersonStatus.FormerAlumni => "FORMER_ALUMNI",
        PersonStatus.Revoked => "REVOKED",
        PersonStatus.Archived => "ARCHIVED",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "unreachable: exhaustive switch"),
    };

    public static bool TryParse(string? value, out PersonStatus status)
    {
        switch (value)
        {
            case "PRE_ENROLLED": status = PersonStatus.PreEnrolled; return true;
            case "VERIFIED": status = PersonStatus.Verified; return true;
            case "ACTIVE": status = PersonStatus.Active; return true;
            case "SUSPENDED": status = PersonStatus.Suspended; return true;
            case "EXPIRED": status = PersonStatus.Expired; return true;
            case "FORMER_ALUMNI": status = PersonStatus.FormerAlumni; return true;
            case "REVOKED": status = PersonStatus.Revoked; return true;
            case "ARCHIVED": status = PersonStatus.Archived; return true;
            default: status = default; return false;
        }
    }
}
