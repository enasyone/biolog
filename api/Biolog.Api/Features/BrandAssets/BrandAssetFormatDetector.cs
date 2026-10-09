namespace Biolog.Api.Features.BrandAssets;

/// <summary>
/// Detects the real format from file content (BIO-CRD-001-AC01), never from the caller's
/// claimed <c>Content-Type</c> or file extension - a mismatched MIME/extension is exactly
/// the disguise the malware-scanning hook (BIO-CRD-001-AC04) and this detection together
/// guard against.
/// </summary>
public static class BrandAssetFormatDetector
{
    public static BrandAssetFormat? Detect(ReadOnlySpan<byte> content)
    {
        if (RasterImageInspector.TryReadPng(content) is not null)
        {
            return BrandAssetFormat.Png;
        }
        if (RasterImageInspector.TryReadJpeg(content) is not null)
        {
            return BrandAssetFormat.Jpeg;
        }
        if (LooksLikeSvg(content))
        {
            return BrandAssetFormat.Svg;
        }
        return null;
    }

    private static bool LooksLikeSvg(ReadOnlySpan<byte> content)
    {
        // A cheap, format-only sniff (is this XML-ish text starting with '<'?); the real
        // structural/active-content decision is SvgValidator's, run separately so an
        // unsupported format and an invalid SVG are reported as two different validation
        // outcomes.
        var trimmed = content;
        while (trimmed.Length > 0 && (trimmed[0] == (byte)' ' || trimmed[0] == (byte)'\t' ||
                                       trimmed[0] == (byte)'\r' || trimmed[0] == (byte)'\n' ||
                                       trimmed[0] == 0xEF)) // skip UTF-8 BOM lead byte and whitespace
        {
            if (trimmed[0] == 0xEF && trimmed.Length >= 3)
            {
                trimmed = trimmed[3..];
                continue;
            }
            if (trimmed[0] == 0xEF)
            {
                break;
            }
            trimmed = trimmed[1..];
        }
        return trimmed.Length > 0 && trimmed[0] == (byte)'<';
    }
}
