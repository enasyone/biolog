using Biolog.Features.TenantFeatureConfig.Identifiers;

namespace Biolog.Features.TenantFeatureConfig.Domain;

/// <summary>
/// BIO-TEN-003-AC02: Biometrics' own nested configuration. <see cref="Enabled"/> (the parent
/// module switch) and <see cref="Face1NIdentificationEnabled"/> are deliberately two
/// independent, separately-settable fields - enabling the parent module never implicitly
/// flips 1:N identification on as a side effect, in this or any later request. This is the
/// real, persisted-in-this-process implementation target for BIO-BIO-007's
/// <c>IFaceSearchFeatureFlagPolicy</c> extension seam (see
/// <c>Integration/TenantFeatureFlagPolicyAdapter.cs</c>).
/// </summary>
public sealed record BiometricsFeatureConfig
{
    public bool Enabled { get; init; }
    public bool Face1NIdentificationEnabled { get; init; }

    public static readonly BiometricsFeatureConfig Disabled = new();
}

/// <summary>
/// BIO-TEN-003-AC01: Document Verify's own nested configuration - the parent module switch
/// plus each of the eight closed <see cref="DocumentVerifySubfeature"/> toggles, each
/// independently settable (never implicitly derived from the parent flag, same discipline as
/// <see cref="BiometricsFeatureConfig"/>).
/// </summary>
public sealed record DocumentVerifyFeatureConfig
{
    public bool Enabled { get; init; }
    public bool CaptureEnabled { get; init; }
    public bool MrzEnabled { get; init; }
    public bool BarcodeEnabled { get; init; }
    public bool OcrEnabled { get; init; }
    public bool AuthenticityEnabled { get; init; }
    public bool DocumentIdentificationEnabled { get; init; }
    public bool NfcEnabled { get; init; }
    public bool SelfieComparisonEnabled { get; init; }

    public static readonly DocumentVerifyFeatureConfig Disabled = new();

    public bool IsEnabled(DocumentVerifySubfeature subfeature) => subfeature switch
    {
        DocumentVerifySubfeature.Capture => CaptureEnabled,
        DocumentVerifySubfeature.Mrz => MrzEnabled,
        DocumentVerifySubfeature.Barcode => BarcodeEnabled,
        DocumentVerifySubfeature.Ocr => OcrEnabled,
        DocumentVerifySubfeature.Authenticity => AuthenticityEnabled,
        DocumentVerifySubfeature.DocumentIdentification => DocumentIdentificationEnabled,
        DocumentVerifySubfeature.Nfc => NfcEnabled,
        DocumentVerifySubfeature.SelfieComparison => SelfieComparisonEnabled,
        _ => throw new ArgumentOutOfRangeException(nameof(subfeature), subfeature, "unreachable: exhaustive switch"),
    };
}

/// <summary>
/// BIO-TEN-003: one tenant's feature-module configuration - the tenant-plane aggregate this
/// story owns, keyed by the tenant id BIO-TEN-001's own control-plane registry mints
/// (<c>ten_...</c>), but never joined against that registry (this feature does not verify the
/// tenant id actually exists there - a disclosed boundary, see
/// docs/features/bio-ten-003/README.md).
///
/// <see cref="Version"/>/<see cref="CreatedAt"/>/<see cref="UpdatedAt"/> mirror BIO-TEN-001's
/// own <c>Tenant</c> envelope shape (ADR-0001 §3.1's mutable-aggregate, CAS-versioned record).
/// <see cref="Version"/> <c>0</c> is a reserved sentinel meaning "never written" - see
/// <see cref="Default"/> - never a real, stored version; the first real write always produces
/// version <c>1</c>.
/// </summary>
public sealed record TenantFeatureConfiguration
{
    public required string TenantId { get; init; }
    public required BiometricsFeatureConfig Biometrics { get; init; }
    public required bool CardProductionEnabled { get; init; }
    public required bool VisitorEnabled { get; init; }
    public required bool AttendanceEnabled { get; init; }
    public required bool TransportEnabled { get; init; }
    public required DocumentVerifyFeatureConfig DocumentVerify { get; init; }
    public required int Version { get; init; }
    public required string CreatedAt { get; init; }
    public required string UpdatedAt { get; init; }

    /// <summary>
    /// BIO-TEN-003-AC04: the fail-closed default for a tenant with no configuration ever
    /// written - every module and subfeature disabled, <see cref="Version"/> <c>0</c> (never
    /// written, never confused with a real stored version 1). This is a value synthesized at
    /// read time, NEVER persisted by this call alone - see
    /// <see cref="TenantFeatureConfigurationService.GetEffectiveAsync"/>.
    /// </summary>
    public static TenantFeatureConfiguration Default(string tenantId)
    {
        var now = UtcInstant.Now();
        return new TenantFeatureConfiguration
        {
            TenantId = tenantId,
            Biometrics = BiometricsFeatureConfig.Disabled,
            CardProductionEnabled = false,
            VisitorEnabled = false,
            AttendanceEnabled = false,
            TransportEnabled = false,
            DocumentVerify = DocumentVerifyFeatureConfig.Disabled,
            Version = 0,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public bool IsModuleEnabled(TenantModule module) => module switch
    {
        TenantModule.Biometrics => Biometrics.Enabled,
        TenantModule.CardProduction => CardProductionEnabled,
        TenantModule.Visitor => VisitorEnabled,
        TenantModule.Attendance => AttendanceEnabled,
        TenantModule.Transport => TransportEnabled,
        TenantModule.DocumentVerify => DocumentVerify.Enabled,
        _ => throw new ArgumentOutOfRangeException(nameof(module), module, "unreachable: exhaustive switch"),
    };
}
