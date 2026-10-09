namespace Biolog.Features.CardTemplates.Domain;

/// <summary>
/// AC03: "retains ... watermarks." Mirrors BIO-CRD-003's own <c>WatermarkPresetKind</c> wire
/// values (<c>api/features/bio-crd-003/Domain/WatermarkPresetCatalog.cs</c>) as this project's
/// own whitelist - BIO-CRD-003 IS a declared dependency, but its catalog is pure static
/// reference data (no repository, no tenant state), so mirroring the small fixed vocabulary
/// here avoids a ProjectReference for zero runtime benefit, the same reasoning
/// <c>CardFormatKind.cs</c> documents for BIO-CRD-002.
/// </summary>
public enum WatermarkPresetKind
{
    None,
    Staff,
    Student,
    Visitor,
}

public static class WatermarkPresetExtensions
{
    public static string ToWireString(this WatermarkPresetKind kind) => kind switch
    {
        WatermarkPresetKind.None => "none",
        WatermarkPresetKind.Staff => "staff",
        WatermarkPresetKind.Student => "student",
        WatermarkPresetKind.Visitor => "visitor",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "unreachable: exhaustive switch"),
    };

    public static bool TryParse(string? value, out WatermarkPresetKind kind)
    {
        switch (value)
        {
            case "none": kind = WatermarkPresetKind.None; return true;
            case "staff": kind = WatermarkPresetKind.Staff; return true;
            case "student": kind = WatermarkPresetKind.Student; return true;
            case "visitor": kind = WatermarkPresetKind.Visitor; return true;
            default: kind = default; return false;
        }
    }
}
