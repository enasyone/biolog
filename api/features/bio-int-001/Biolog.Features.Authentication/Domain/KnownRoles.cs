namespace Biolog.Features.Authentication;

/// <summary>
/// Mirrors BIO-IDN-006's own closed <c>RoleType</c> wire-string set by VALUE, not by
/// reference - this project cannot hold a <c>ProjectReference</c> to bio-idn-006's csproj
/// (only <c>Biolog.Api</c> may reference feature projects), and even the host can only see
/// bio-idn-006's role-lookup surface through its own internal <c>PermissionService</c>
/// (InternalsVisibleTo scoped to Biolog.Api and bio-idn-006's own tests only). A
/// <see cref="ClaimMappingRule"/>'s <c>MappedRole</c> is only valid if it names one of these
/// eleven wire strings, keeping this story's claim-mapping output semantically compatible
/// with BIO-IDN-006's role model without a live cross-project dependency. If BIO-IDN-006
/// ever adds a twelfth role, this set is a documented, deliberate duplication that needs
/// updating alongside it - not an accidental drift risk this story is unaware of.
/// </summary>
public static class KnownRoles
{
    public static readonly IReadOnlySet<string> WireFormats = new HashSet<string>(StringComparer.Ordinal)
    {
        "SuperAdmin",
        "TenantAdmin",
        "RegistrarHr",
        "Enrolment",
        "Biometric",
        "CardProduction",
        "Security",
        "Gate",
        "Auditor",
        "Helpdesk",
        "ReadOnly",
    };

    public static bool IsKnown(string roleWireFormat) => WireFormats.Contains(roleWireFormat);
}
