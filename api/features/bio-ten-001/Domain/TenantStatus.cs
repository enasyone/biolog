namespace Biolog.Features.TenantRegistry.Domain;

/// <summary>
/// BIO-TEN-001-AC03: a tenant's control-plane status. Exactly these three values - no
/// "pending", no "deleted", no free-form string. A caller that supplies anything else gets
/// a validation error (see <see cref="TenantStatusExtensions.Parse"/>), never a silently
/// coerced default.
/// </summary>
public enum TenantStatus
{
    Active,
    Suspended,
    Disabled,
}

public static class TenantStatusExtensions
{
    /// <summary>Wire form: lowercase, matching every other enum-as-string in this platform's contracts.</summary>
    public static string ToWireString(this TenantStatus status) => status switch
    {
        TenantStatus.Active => "active",
        TenantStatus.Suspended => "suspended",
        TenantStatus.Disabled => "disabled",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "unreachable: exhaustive switch"),
    };

    /// <summary>Parses the wire form. Returns false, rather than throwing, on anything unrecognised.</summary>
    public static bool TryParse(string? value, out TenantStatus status)
    {
        switch (value)
        {
            case "active": status = TenantStatus.Active; return true;
            case "suspended": status = TenantStatus.Suspended; return true;
            case "disabled": status = TenantStatus.Disabled; return true;
            default: status = default; return false;
        }
    }
}
