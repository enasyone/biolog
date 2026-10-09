namespace Biolog.Features.Rbac.Domain;

/// <summary>
/// BIO-IDN-006-AC04: role changes are audited, and this story preserves the FACT that a role was
/// once held rather than deleting the record on revoke - the same "governed transition, never a
/// delete" discipline BIO-IDN-003's <c>IdentifierStatus</c> and BIO-IDN-001's
/// <c>PersonStatus</c>-to-<c>Archived</c> both apply.
/// </summary>
public enum RoleAssignmentStatus
{
    Active,
    Revoked,
}

public static class RoleAssignmentStatusExtensions
{
    public static string ToWireString(this RoleAssignmentStatus status) => status switch
    {
        RoleAssignmentStatus.Active => "ACTIVE",
        RoleAssignmentStatus.Revoked => "REVOKED",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "unreachable: exhaustive switch"),
    };
}
