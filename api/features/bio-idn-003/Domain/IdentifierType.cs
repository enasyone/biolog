namespace Biolog.Features.InstitutionalIdentifiers.Domain;

/// <summary>
/// BIO-IDN-003-AC01. The user story names "matriculation, employee and other business
/// identifiers" verbatim. <see cref="TenantDefined"/> covers "other" - a tenant-specific type
/// not in the fixed list, named via <see cref="InstitutionalIdentifier.CustomLabel"/> rather
/// than a full tenant-configurable type registry (no AC or ADR describes one; out of scope,
/// matching BIO-IDN-002's <c>ProfileType.TenantDefined</c> precedent exactly).
/// </summary>
public enum IdentifierType
{
    MatriculationNumber,
    StaffNumber,
    TenantDefined,
}

public static class IdentifierTypeExtensions
{
    public static string ToWireString(this IdentifierType type) => type switch
    {
        IdentifierType.MatriculationNumber => "MATRICULATION_NUMBER",
        IdentifierType.StaffNumber => "STAFF_NUMBER",
        IdentifierType.TenantDefined => "TENANT_DEFINED",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "unreachable: exhaustive switch"),
    };

    public static bool TryParse(string? value, out IdentifierType type)
    {
        switch (value)
        {
            case "MATRICULATION_NUMBER": type = IdentifierType.MatriculationNumber; return true;
            case "STAFF_NUMBER": type = IdentifierType.StaffNumber; return true;
            case "TENANT_DEFINED": type = IdentifierType.TenantDefined; return true;
            default: type = default; return false;
        }
    }
}
