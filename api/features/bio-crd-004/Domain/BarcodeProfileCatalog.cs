namespace Biolog.Features.CardObjects.Domain;

/// <summary>
/// BIO-CRD-004-AC01 ("Default ID-card barcode profile is alphanumeric") and AC02 ("QR/PDF417/
/// Data Matrix/Aztec are optional profiles, not default"). A fixed, stateless, non-tenant-scoped
/// reference catalogue - the same shape BIO-CRD-002's own <c>CardFormatCatalog</c> and
/// BIO-CRD-003's own <c>WatermarkPresetCatalog</c> both use for fixed baseline reference data.
/// </summary>
public static class BarcodeProfileCatalog
{
    /// <summary>AC01: the only profile a new barcode object starts with unless a designer
    /// explicitly picks one of <see cref="OptionalProfiles"/>.</summary>
    public const BarcodeProfile DefaultProfile = BarcodeProfile.Alphanumeric;

    /// <summary>AC02: every profile other than the default - never auto-selected.</summary>
    public static readonly IReadOnlyList<BarcodeProfile> OptionalProfiles =
    [
        BarcodeProfile.Qr,
        BarcodeProfile.Pdf417,
        BarcodeProfile.DataMatrix,
        BarcodeProfile.Aztec,
    ];

    public static readonly IReadOnlyList<BarcodeProfile> AllProfiles =
        [DefaultProfile, .. OptionalProfiles];

    public static bool IsDefault(BarcodeProfile profile) => profile == DefaultProfile;
}
