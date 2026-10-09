namespace Biolog.Features.CardTemplates.Domain;

/// <summary>
/// AC03: "retains ... barcode ... rules." Mirrors BIO-CRD-004's own <c>BarcodeProfile</c> wire
/// values (<c>api/features/bio-crd-004/Domain/BarcodeProfileCatalog.cs</c>) as this project's
/// own whitelist - same reasoning as <c>WatermarkPresetKind.cs</c>. <see cref="None"/> means
/// this template carries no barcode element at all.
/// </summary>
public enum BarcodeProfileKind
{
    None,
    Alphanumeric,
    Qr,
    Pdf417,
    DataMatrix,
    Aztec,
}

public static class BarcodeProfileExtensions
{
    public static string ToWireString(this BarcodeProfileKind kind) => kind switch
    {
        BarcodeProfileKind.None => "none",
        BarcodeProfileKind.Alphanumeric => "alphanumeric",
        BarcodeProfileKind.Qr => "qr",
        BarcodeProfileKind.Pdf417 => "pdf417",
        BarcodeProfileKind.DataMatrix => "data_matrix",
        BarcodeProfileKind.Aztec => "aztec",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "unreachable: exhaustive switch"),
    };

    public static bool TryParse(string? value, out BarcodeProfileKind kind)
    {
        switch (value)
        {
            case "none": kind = BarcodeProfileKind.None; return true;
            case "alphanumeric": kind = BarcodeProfileKind.Alphanumeric; return true;
            case "qr": kind = BarcodeProfileKind.Qr; return true;
            case "pdf417": kind = BarcodeProfileKind.Pdf417; return true;
            case "data_matrix": kind = BarcodeProfileKind.DataMatrix; return true;
            case "aztec": kind = BarcodeProfileKind.Aztec; return true;
            default: kind = default; return false;
        }
    }
}
