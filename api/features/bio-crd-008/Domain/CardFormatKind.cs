namespace Biolog.Features.CardTemplates.Domain;

/// <summary>
/// AC03: "retains card size/orientation." Mirrors BIO-CRD-002's own client-side
/// <c>CardFormatKind</c> wire values (<c>web/features/bio-crd-002/cardFormats.ts</c>) as this
/// project's own whitelist, the same "not a declared dependency, so own the vocabulary" shape
/// BIO-RPT-002 established for <c>ReportDataset</c> - BIO-CRD-002 is sequencing only
/// (`card-designer` conflict group), not a declared dependency of this story (BIO-CRD-003,
/// BIO-CRD-004 are).
/// </summary>
public enum CardFormatKind
{
    Cr80,
    Cr79,
    Cr100,
    Custom,
}

public enum CardOrientation
{
    Portrait,
    Landscape,
}

public static class CardFormatExtensions
{
    public static string ToWireString(this CardFormatKind kind) => kind switch
    {
        CardFormatKind.Cr80 => "cr80",
        CardFormatKind.Cr79 => "cr79",
        CardFormatKind.Cr100 => "cr100",
        CardFormatKind.Custom => "custom",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "unreachable: exhaustive switch"),
    };

    public static bool TryParse(string? value, out CardFormatKind kind)
    {
        switch (value)
        {
            case "cr80": kind = CardFormatKind.Cr80; return true;
            case "cr79": kind = CardFormatKind.Cr79; return true;
            case "cr100": kind = CardFormatKind.Cr100; return true;
            case "custom": kind = CardFormatKind.Custom; return true;
            default: kind = default; return false;
        }
    }

    public static string ToWireString(this CardOrientation orientation) => orientation switch
    {
        CardOrientation.Portrait => "portrait",
        CardOrientation.Landscape => "landscape",
        _ => throw new ArgumentOutOfRangeException(nameof(orientation), orientation, "unreachable: exhaustive switch"),
    };

    /// <summary>Named distinctly from the <see cref="CardFormatKind"/> overload above (not just differing by out-param type) - two <c>TryParse</c> overloads that differ only in their <c>out</c> parameter's type are genuinely ambiguous at an <c>out var</c> call site (the compiler cannot pick between them without already knowing the inferred type), a real bug caught before this ever compiled.</summary>
    public static bool TryParseOrientation(string? value, out CardOrientation orientation)
    {
        switch (value)
        {
            case "portrait": orientation = CardOrientation.Portrait; return true;
            case "landscape": orientation = CardOrientation.Landscape; return true;
            default: orientation = default; return false;
        }
    }
}
