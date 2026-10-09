using System.Buffers.Binary;

namespace Biolog.Api.Features.BrandAssets;

/// <summary>
/// Parses PNG/JPEG headers to compute the raster quality assessment (BIO-CRD-001-AC03),
/// per Card Designer v0.3 §9.2: pixel dimensions, alpha support, and effective DPI at the
/// intended physical print size. Implemented as a minimal, dependency-free header reader -
/// deliberately not an image-decoding library dependency - reading only the small,
/// well-documented header structures both formats already require every decoder to parse:
/// PNG's IHDR/pHYs chunks and JPEG's JFIF APP0/SOFn segments.
/// </summary>
public static class RasterImageInspector
{
    /// <summary>A commonly used print-quality floor. Below this, print output visibly softens.</summary>
    public const double LowResolutionDpiThreshold = 150.0;

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public readonly record struct RasterHeader(int Width, int Height, bool HasAlpha, double? NativeDpi);

    public static RasterHeader? TryReadPng(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 8 || !bytes[..8].SequenceEqual(PngSignature))
        {
            return null;
        }

        var offset = 8;
        int? width = null, height = null;
        bool? hasAlpha = null;
        double? nativeDpi = null;

        while (offset + 8 <= bytes.Length)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(offset, 4));
            var type = System.Text.Encoding.ASCII.GetString(bytes.Slice(offset + 4, 4));
            var dataStart = offset + 8;
            if ((long)dataStart + length > bytes.Length)
            {
                break; // truncated/corrupt chunk; stop rather than read out of bounds
            }
            var data = bytes.Slice(dataStart, (int)length);

            if (type == "IHDR" && data.Length >= 13)
            {
                width = (int)BinaryPrimitives.ReadUInt32BigEndian(data[..4]);
                height = (int)BinaryPrimitives.ReadUInt32BigEndian(data.Slice(4, 4));
                var colorType = data[9];
                hasAlpha = colorType is 4 or 6;
            }
            else if (type == "pHYs" && data.Length >= 9)
            {
                var ppuX = BinaryPrimitives.ReadUInt32BigEndian(data[..4]);
                var unit = data[8];
                if (unit == 1 && ppuX > 0) // pixels per metre
                {
                    nativeDpi = ppuX * 0.0254;
                }
            }
            else if (type == "IDAT" || type == "IEND")
            {
                break; // header chunks are always before image data; nothing further to read
            }

            offset = dataStart + (int)length + 4; // skip CRC
        }

        if (width is null || height is null || hasAlpha is null)
        {
            return null;
        }
        return new RasterHeader(width.Value, height.Value, hasAlpha.Value, nativeDpi);
    }

    public static RasterHeader? TryReadJpeg(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 4 || bytes[0] != 0xFF || bytes[1] != 0xD8)
        {
            return null;
        }

        var offset = 2;
        int? width = null, height = null;
        double? nativeDpi = null;

        while (offset + 4 <= bytes.Length)
        {
            if (bytes[offset] != 0xFF)
            {
                offset++;
                continue; // fill byte between segments
            }
            var marker = bytes[offset + 1];
            if (marker == 0xD8 || marker == 0x01 || (marker >= 0xD0 && marker <= 0xD9))
            {
                offset += 2; // standalone marker, no length field
                continue;
            }
            if (offset + 4 > bytes.Length)
            {
                break;
            }
            var segmentLength = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset + 2, 2));
            if (segmentLength < 2 || offset + 2 + segmentLength > bytes.Length)
            {
                break;
            }
            var segment = bytes.Slice(offset + 4, segmentLength - 2);

            var isSofMarker = marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC;
            if (isSofMarker && segment.Length >= 5)
            {
                height = BinaryPrimitives.ReadUInt16BigEndian(segment.Slice(1, 2));
                width = BinaryPrimitives.ReadUInt16BigEndian(segment.Slice(3, 2));
            }
            else if (marker == 0xE0 && segment.Length >= 12 && segment[..5].SequenceEqual("JFIF\0"u8))
            {
                var units = segment[7];
                var xDensity = BinaryPrimitives.ReadUInt16BigEndian(segment.Slice(8, 2));
                nativeDpi = units switch
                {
                    1 => xDensity, // dots per inch
                    2 => xDensity * 2.54, // dots per cm
                    _ => null, // 0: aspect ratio only, not a real density
                };
            }

            if (marker == 0xD9) // EOI
            {
                break;
            }
            offset += 2 + segmentLength;
        }

        if (width is null || height is null)
        {
            return null;
        }
        return new RasterHeader(width.Value, height.Value, false, nativeDpi);
    }

    /// <summary>
    /// Builds the full quality assessment for a raster header (BIO-CRD-001-AC03).
    /// <paramref name="intendedWidthMm"/> is the caller-declared intended print width, if
    /// known at upload time; effective DPI prefers it over the file's own native DPI
    /// metadata, because it reflects how the asset will actually be printed.
    /// </summary>
    public static RasterQualityAssessment Assess(RasterHeader header, double? intendedWidthMm)
    {
        double? effectiveDpi = intendedWidthMm is > 0
            ? header.Width / (intendedWidthMm.Value / 25.4)
            : header.NativeDpi;

        var lowResolution = effectiveDpi is not null && effectiveDpi < LowResolutionDpiThreshold;
        string? message = lowResolution
            ? $"Effective resolution ~{effectiveDpi:F0} DPI is below the {LowResolutionDpiThreshold:F0} DPI print-quality floor."
            : null;

        return new RasterQualityAssessment(
            header.Width, header.Height, header.HasAlpha, header.NativeDpi, effectiveDpi, lowResolution, message);
    }
}
