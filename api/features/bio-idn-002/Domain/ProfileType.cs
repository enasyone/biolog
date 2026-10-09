namespace Biolog.Features.PersonProfileRegistry.Domain;

/// <summary>
/// BIO-IDN-002-AC01. The profile types the user story names verbatim: "Student, Employee,
/// Visitor, Contractor, Alumni, Parent or tenant-defined profile." <see cref="TenantDefined"/>
/// covers the last case: a tenant-specific type not in the fixed list, named via
/// <see cref="PersonProfile.CustomLabel"/> rather than a full tenant-configurable profile-type
/// registry (no AC or ADR describes one; out of scope, see README "Design decisions and scope
/// boundaries"). "Employee" here covers the spec's "Staff" language too - the master spec §4
/// and this story's own user story use the two interchangeably ("Student/Staff/Employee" vs.
/// "Student, Employee").
/// </summary>
public enum ProfileType
{
    Student,
    Employee,
    Visitor,
    Contractor,
    Alumni,
    Parent,
    TenantDefined,
}

public static class ProfileTypeExtensions
{
    public static string ToWireString(this ProfileType type) => type switch
    {
        ProfileType.Student => "STUDENT",
        ProfileType.Employee => "EMPLOYEE",
        ProfileType.Visitor => "VISITOR",
        ProfileType.Contractor => "CONTRACTOR",
        ProfileType.Alumni => "ALUMNI",
        ProfileType.Parent => "PARENT",
        ProfileType.TenantDefined => "TENANT_DEFINED",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "unreachable: exhaustive switch"),
    };

    public static bool TryParse(string? value, out ProfileType type)
    {
        switch (value)
        {
            case "STUDENT": type = ProfileType.Student; return true;
            case "EMPLOYEE": type = ProfileType.Employee; return true;
            case "VISITOR": type = ProfileType.Visitor; return true;
            case "CONTRACTOR": type = ProfileType.Contractor; return true;
            case "ALUMNI": type = ProfileType.Alumni; return true;
            case "PARENT": type = ProfileType.Parent; return true;
            case "TENANT_DEFINED": type = ProfileType.TenantDefined; return true;
            default: type = default; return false;
        }
    }
}
