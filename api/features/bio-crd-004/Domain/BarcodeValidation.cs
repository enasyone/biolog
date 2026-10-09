namespace Biolog.Features.CardObjects.Domain;

/// <summary>
/// BIO-CRD-004-AC03: "Barcode objects validate quiet zone/size/contrast." As with
/// BIO-CRD-002's card-format dimensions and BIO-CRD-003's watermark presets, no "Card Designer
/// v0.3"-style specification naming exact barcode thresholds exists anywhere in this repository.
/// The bounds below are this story's own deliberately-named starting point:
///
/// <list type="bullet">
/// <item><see cref="MinQuietZoneMm"/> (2.0mm) - a conservative minimum quiet-zone margin,
/// comfortably inside the range published by common linear/2D symbology quiet-zone
/// recommendations (typically 10x-module-width for linear codes, a handful of millimetres in
/// practice at ID-card print scale).</item>
/// <item><see cref="MinDimensionMm"/>/<see cref="MaxDimensionMm"/> (5mm-100mm) - a barcode
/// object must be large enough to scan reliably and never larger than a whole CR100 card
/// (BIO-CRD-002's own largest standard format's long edge).</item>
/// <item><see cref="MinContrastRatio"/> (4.5:1) - this repository has no barcode-specific
/// print-contrast-signal figure on file, so this validator borrows the WCAG 2.x AA "normal
/// text" contrast-ratio threshold as a disclosed, testable proxy for "distinguishable enough to
/// scan", computed from the same relative-luminance formula WCAG defines
/// (<see cref="RelativeLuminance"/>/<see cref="ContrastRatio"/>).</item>
/// </list>
///
/// Validation never throws for a well-formed request - an invalid barcode configuration is
/// DATA (<see cref="BarcodeValidationResult.IsValid"/> false with named violations), never an
/// HTTP error, the same convention BIO-CRD-002-AC03's custom-size validator established.
/// </summary>
public sealed record BarcodeValidationRequest(
    BarcodeProfile Profile,
    double QuietZoneMm,
    double WidthMm,
    double HeightMm,
    string ForegroundColorHex,
    string BackgroundColorHex);

public sealed record BarcodeValidationResult(bool IsValid, IReadOnlyList<string> Violations)
{
    public static BarcodeValidationResult Valid() => new(true, []);
    public static BarcodeValidationResult Invalid(IReadOnlyList<string> violations) => new(false, violations);
}

public static class BarcodeValidation
{
    public const double MinQuietZoneMm = 2.0;
    public const double MinDimensionMm = 5.0;
    public const double MaxDimensionMm = 100.0;
    public const double MinContrastRatio = 4.5;

    public static BarcodeValidationResult Validate(BarcodeValidationRequest request)
    {
        var violations = new List<string>();

        if (!double.IsFinite(request.QuietZoneMm) || request.QuietZoneMm < MinQuietZoneMm)
        {
            violations.Add($"quiet_zone_mm must be at least {MinQuietZoneMm}");
        }

        if (!double.IsFinite(request.WidthMm) || request.WidthMm < MinDimensionMm || request.WidthMm > MaxDimensionMm)
        {
            violations.Add($"width_mm must be between {MinDimensionMm} and {MaxDimensionMm}");
        }

        if (!double.IsFinite(request.HeightMm) || request.HeightMm < MinDimensionMm || request.HeightMm > MaxDimensionMm)
        {
            violations.Add($"height_mm must be between {MinDimensionMm} and {MaxDimensionMm}");
        }

        if (!TryParseHexColor(request.ForegroundColorHex, out var foreground))
        {
            violations.Add("foreground_color_hex must be a well-formed #rrggbb colour");
        }

        if (!TryParseHexColor(request.BackgroundColorHex, out var background))
        {
            violations.Add("background_color_hex must be a well-formed #rrggbb colour");
        }

        if (violations.Count == 0)
        {
            var ratio = ContrastRatio(foreground, background);
            if (ratio < MinContrastRatio)
            {
                violations.Add($"contrast_ratio ({ratio:F2}:1) must be at least {MinContrastRatio}:1");
            }
        }

        return violations.Count == 0 ? BarcodeValidationResult.Valid() : BarcodeValidationResult.Invalid(violations);
    }

    /// <summary>WCAG 2.x contrast ratio: (L1 + 0.05) / (L2 + 0.05), lighter over darker.</summary>
    public static double ContrastRatio((double R, double G, double B) a, (double R, double G, double B) b)
    {
        var la = RelativeLuminance(a);
        var lb = RelativeLuminance(b);
        var lighter = Math.Max(la, lb);
        var darker = Math.Min(la, lb);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double RelativeLuminance((double R, double G, double B) rgb)
    {
        double Channel(double c)
        {
            var srgb = c / 255.0;
            return srgb <= 0.03928 ? srgb / 12.92 : Math.Pow((srgb + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(rgb.R) + 0.7152 * Channel(rgb.G) + 0.0722 * Channel(rgb.B);
    }

    public static bool TryParseHexColor(string? hex, out (double R, double G, double B) rgb)
    {
        rgb = default;
        if (hex is null || hex.Length != 7 || hex[0] != '#')
        {
            return false;
        }

        if (!byte.TryParse(hex.AsSpan(1, 2), System.Globalization.NumberStyles.HexNumber, null, out var r) ||
            !byte.TryParse(hex.AsSpan(3, 2), System.Globalization.NumberStyles.HexNumber, null, out var g) ||
            !byte.TryParse(hex.AsSpan(5, 2), System.Globalization.NumberStyles.HexNumber, null, out var b))
        {
            return false;
        }

        rgb = (r, g, b);
        return true;
    }
}
