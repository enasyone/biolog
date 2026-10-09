namespace Biolog.Features.CardObjects.Domain;

/// <summary>
/// BIO-CRD-004-AC01/AC02: the symbology a barcode card object renders as. <see cref="Alphanumeric"/>
/// is the only default-eligible profile (a linear, alphanumeric-capable symbology, e.g.
/// Code 128); the four 2D symbologies are optional profiles a designer must explicitly choose -
/// see <see cref="BarcodeProfileCatalog"/>.
/// </summary>
public enum BarcodeProfile
{
    Alphanumeric,
    Qr,
    Pdf417,
    DataMatrix,
    Aztec,
}

public static class BarcodeProfileExtensions
{
    public static string ToWireString(this BarcodeProfile profile) => profile switch
    {
        BarcodeProfile.Alphanumeric => "alphanumeric",
        BarcodeProfile.Qr => "qr",
        BarcodeProfile.Pdf417 => "pdf417",
        BarcodeProfile.DataMatrix => "data_matrix",
        BarcodeProfile.Aztec => "aztec",
        _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, "unrecognised barcode profile"),
    };

    public static bool TryParse(string? value, out BarcodeProfile profile)
    {
        switch (value)
        {
            case "alphanumeric": profile = BarcodeProfile.Alphanumeric; return true;
            case "qr": profile = BarcodeProfile.Qr; return true;
            case "pdf417": profile = BarcodeProfile.Pdf417; return true;
            case "data_matrix": profile = BarcodeProfile.DataMatrix; return true;
            case "aztec": profile = BarcodeProfile.Aztec; return true;
            default: profile = default; return false;
        }
    }
}
