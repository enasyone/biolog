namespace Biolog.Features.PersonRegistry.Domain;

/// <summary>
/// BIO-IDN-001-AC03. The eight identity lifecycle states named verbatim by the Master
/// Technical Specification §4 ("Universal identity"): "PRE_ENROLLED, VERIFIED, ACTIVE,
/// SUSPENDED, EXPIRED, FORMER/ALUMNI, REVOKED, ARCHIVED." The spec's single slashed token
/// "FORMER/ALUMNI" is rendered here as one state, <see cref="FormerAlumni"/> /
/// <c>FORMER_ALUMNI</c> on the wire - the spec names it as one lifecycle position (a person
/// who has left the tenant but remains historically relevant), not two, and BIO-IDN-002's own
/// separate "Alumni" PROFILE type (a role a person can hold) is a different concept at a
/// different layer: this is the person's own life-cycle position, not a role attached to them.
///
/// See <see cref="Person"/>'s class header for what this story's own transition table permits
/// and what BIO-IDN-004 ("Implement identity lifecycle and emergency suspension") is expected
/// to layer on top of it.
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
