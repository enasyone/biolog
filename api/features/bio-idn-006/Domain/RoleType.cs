namespace Biolog.Features.Rbac.Domain;

/// <summary>
/// BIO-IDN-006-AC01: the eleven baseline operational roles named verbatim by the approved
/// story: "Super Admin, Tenant Admin, Registrar/HR, Enrolment, Biometric, Card Production,
/// Security, Gate, Auditor, Helpdesk and Read-only." A closed enum, not a tenant-extensible
/// catalogue - AC01 names a FIXED baseline set, unlike BIO-IDN-002's <c>ProfileType</c> or
/// BIO-IDN-003's <c>IdentifierType</c>, which explicitly needed a tenant-defined escape hatch
/// their own ACs asked for. AC02's "permissions are tenant-scoped" is satisfied by
/// <see cref="RolePermissionGrant"/> (WHICH permissions a role carries is tenant-configurable),
/// not by making the role NAMES themselves tenant-configurable.
///
/// <b><see cref="SuperAdmin"/> is special</b>: it implicitly holds every permission, always,
/// and is deliberately NOT configurable via <see cref="RolePermissionGrant"/> - see that type's
/// header and <see cref="PermissionService.HasPermissionAsync"/> for why (a tenant must never be
/// able to accidentally strip its own top-level administrator's authority via a permission-grant
/// misconfiguration).
/// </summary>
public enum RoleType
{
    SuperAdmin,
    TenantAdmin,
    RegistrarHr,
    Enrolment,
    Biometric,
    CardProduction,
    Security,
    Gate,
    Auditor,
    Helpdesk,
    ReadOnly,
}

public static class RoleTypeExtensions
{
    public static string ToWireString(this RoleType role) => role switch
    {
        RoleType.SuperAdmin => "SUPER_ADMIN",
        RoleType.TenantAdmin => "TENANT_ADMIN",
        RoleType.RegistrarHr => "REGISTRAR_HR",
        RoleType.Enrolment => "ENROLMENT",
        RoleType.Biometric => "BIOMETRIC",
        RoleType.CardProduction => "CARD_PRODUCTION",
        RoleType.Security => "SECURITY",
        RoleType.Gate => "GATE",
        RoleType.Auditor => "AUDITOR",
        RoleType.Helpdesk => "HELPDESK",
        RoleType.ReadOnly => "READ_ONLY",
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "unreachable: exhaustive switch"),
    };

    public static bool TryParse(string? value, out RoleType role)
    {
        switch (value)
        {
            case "SUPER_ADMIN": role = RoleType.SuperAdmin; return true;
            case "TENANT_ADMIN": role = RoleType.TenantAdmin; return true;
            case "REGISTRAR_HR": role = RoleType.RegistrarHr; return true;
            case "ENROLMENT": role = RoleType.Enrolment; return true;
            case "BIOMETRIC": role = RoleType.Biometric; return true;
            case "CARD_PRODUCTION": role = RoleType.CardProduction; return true;
            case "SECURITY": role = RoleType.Security; return true;
            case "GATE": role = RoleType.Gate; return true;
            case "AUDITOR": role = RoleType.Auditor; return true;
            case "HELPDESK": role = RoleType.Helpdesk; return true;
            case "READ_ONLY": role = RoleType.ReadOnly; return true;
            default: role = default; return false;
        }
    }

    /// <summary>Every baseline role, in registration order - AC01's own catalogue, exposed for a read-only listing endpoint.</summary>
    public static readonly IReadOnlyList<RoleType> All =
    [
        RoleType.SuperAdmin, RoleType.TenantAdmin, RoleType.RegistrarHr, RoleType.Enrolment, RoleType.Biometric,
        RoleType.CardProduction, RoleType.Security, RoleType.Gate, RoleType.Auditor, RoleType.Helpdesk, RoleType.ReadOnly,
    ];
}
