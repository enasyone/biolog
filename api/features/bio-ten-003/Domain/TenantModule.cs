namespace Biolog.Features.TenantFeatureConfig.Domain;

/// <summary>
/// BIO-TEN-003-AC01: the closed set of selectable top-level feature modules a tenant admin
/// can enable/disable. Exactly these six - named and ordered per the story spec, cross-checked
/// against <c>web/lib/feature-mode.ts</c>'s own <c>ServicePort</c> naming so this enum's wire
/// form is traceable to a real frontend module (see docs/features/bio-ten-003/README.md
/// "ServicePort mapping"). No free-form module name can ever be stored - an unrecognised
/// value is a validation error (<see cref="TenantModuleExtensions.TryParse"/>), never silently
/// coerced or dropped.
///
/// <see cref="Biometrics"/> and <see cref="DocumentVerify"/> each carry additional nested
/// configuration beyond a plain on/off flag (see <see cref="BiometricsFeatureConfig"/>,
/// <see cref="DocumentVerifyFeatureConfig"/>); the other four are plain booleans on
/// <see cref="TenantFeatureConfiguration"/>.
/// </summary>
public enum TenantModule
{
    Biometrics,
    CardProduction,
    Visitor,
    Attendance,
    Transport,
    DocumentVerify,
}

public static class TenantModuleExtensions
{
    /// <summary>
    /// Wire form, snake_case (matching every other enum-as-string in this feature's own
    /// contracts). Also doubles as the traceability key to this module's frontend
    /// <c>ServicePort</c> name (documented in docs/features/bio-ten-003/README.md): Biometrics
    /// -&gt; <c>BiometricService</c>, CardProduction -&gt; <c>CredentialService</c> (card
    /// production is modelled under the Credential service port in
    /// <c>web/lib/route-manifest.ts</c>), Visitor -&gt; <c>VisitorService</c>, Attendance -&gt;
    /// <c>AttendanceService</c>, Transport -&gt; <c>TransportService</c>, DocumentVerify -&gt;
    /// <c>DocumentVerifyService</c>.
    /// </summary>
    public static string ToWireString(this TenantModule module) => module switch
    {
        TenantModule.Biometrics => "biometrics",
        TenantModule.CardProduction => "card_production",
        TenantModule.Visitor => "visitor",
        TenantModule.Attendance => "attendance",
        TenantModule.Transport => "transport",
        TenantModule.DocumentVerify => "document_verify",
        _ => throw new ArgumentOutOfRangeException(nameof(module), module, "unreachable: exhaustive switch"),
    };

    /// <summary>Parses the wire form. Returns false, rather than throwing, on anything unrecognised.</summary>
    public static bool TryParse(string? value, out TenantModule module)
    {
        switch (value)
        {
            case "biometrics": module = TenantModule.Biometrics; return true;
            case "card_production": module = TenantModule.CardProduction; return true;
            case "visitor": module = TenantModule.Visitor; return true;
            case "attendance": module = TenantModule.Attendance; return true;
            case "transport": module = TenantModule.Transport; return true;
            case "document_verify": module = TenantModule.DocumentVerify; return true;
            default: module = default; return false;
        }
    }
}
