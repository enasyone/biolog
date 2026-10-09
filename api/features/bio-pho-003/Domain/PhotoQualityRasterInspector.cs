using System.Buffers.Binary;

namespace Biolog.Features.PhotoQuality.Domain;

/// <summary>
/// Dependency-free format detection, dimension and native-DPI reading for the two formats this
/// story supports (BIO-PHO-003-AC01's print-quality side). Format is always detected from the
/// file's own magic bytes - a caller-supplied content type is never trusted. A fresh,
/// self-contained copy, mirroring <c>PhotoRasterInspector</c> (BIO-PHO-001) and
/// <c>CaptureRasterInspector</c> (BIO-PHO-002) for this feature's own asset class - no
/// cross-feature reference exists (or is allowed) between any of them, the established
/// convention in this repository.
///
/// <b>No pixel resampling or content analysis here, or anywhere in this project - a disclosed
/// scope boundary (BIO-PHO-003-AC04).</b> No image-processing or computer-vision library is
/// referenced anywhere in this project. This class reads header metadata only (format, pixel
/// dimensions, native DPI where present) - the same "parse the header, never decode/re-encode
/// pixels" boundary <c>PhotoRasterInspector</c>/<c>CaptureRasterInspector</c> already
/// established. Categorically, nothing in this project can apply a beauty/facial-reshaping
/// filter, because nothing in this project can decode a pixel at all.
/// </summary>
public static class PhotoQualityRasterInspector
{
    private static readonly byte[] PngMagic = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public sealed record Inspected(PhotoFormat Format, int WidthPx, int HeightPx, double? NativeDpi);

    public static Inspected? Inspect(ReadOnlySpan<byte> content)
    {
        if (TryReadPng(content, out var pngResult))
        {
            return pngResult;
        }

        if (TryReadJpeg(content, out var jpegResult))
        {
            return jpegResult;
        }

        return null;
    }

    private static bool TryReadPng(ReadOnlySpan<byte> content, out Inspected? result)
    {
        result = null;
        if (content.Length < 8 + 8 + 8 || !content[..8].SequenceEqual(PngMagic))
        {
            return false;
        }

        if (content[12] != (byte)'I' || content[13] != (byte)'H' || content[14] != (byte)'D' || content[15] != (byte)'R')
        {
            return false;
        }

        var width = (int)BinaryPrimitives.ReadUInt32BigEndian(content.Slice(16, 4));
        var height = (int)BinaryPrimitives.ReadUInt32BigEndian(content.Slice(20, 4));
        if (width <= 0 || height <= 0)
        {
            return false;
        }

        double? dpi = null;
        var offset = 8;
        while (offset + 12 <= content.Length)
        {
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(content.Slice(offset, 4));
            var type = content.Slice(offset + 4, 4);
            if (type.SequenceEqual("pHYs"u8) && length >= 9 && offset + 8 + 9 <= content.Length)
            {
                var pixelsPerUnitX = BinaryPrimitives.ReadUInt32BigEndian(content.Slice(offset + 8, 4));
                var unitSpecifier = content[offset + 16];
                if (unitSpecifier == 1) // metre
                {
                    dpi = pixelsPerUnitX * 0.0254;
                }

                break;
            }

            if (type.SequenceEqual("IDAT"u8))
            {
                break; // pHYs, if present, always precedes the first IDAT chunk (PNG spec)
            }

            offset += 12 + length; // 4 length + 4 type + data + 4 crc
        }

        result = new Inspected(PhotoFormat.Png, width, height, dpi);
        return true;
    }

    private static bool TryReadJpeg(ReadOnlySpan<byte> content, out Inspected? result)
    {
        result = null;
        if (content.Length < 4 || content[0] != 0xFF || content[1] != 0xD8)
        {
            return false;
        }

        int? width = null, height = null;
        double? dpi = null;
        var offset = 2;
        while (offset + 4 <= content.Length)
        {
            if (content[offset] != 0xFF)
            {
                break;
            }

            var marker = content[offset + 1];
            if (marker == 0xD8 || marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7))
            {
                offset += 2;
                continue;
            }

            if (marker == 0xD9 || offset + 4 > content.Length)
            {
                break;
            }

            var segmentLength = BinaryPrimitives.ReadUInt16BigEndian(content.Slice(offset + 2, 2));
            var isSof = marker is >= 0xC0 and <= 0xCF && marker is not 0xC4 and not 0xC8 and not 0xCC;

            if (isSof && offset + 2 + 2 + 5 <= content.Length)
            {
                height = BinaryPrimitives.ReadUInt16BigEndian(content.Slice(offset + 5, 2));
                width = BinaryPrimitives.ReadUInt16BigEndian(content.Slice(offset + 7, 2));
                break; // dimensions found; APP0/JFIF (if present) always precedes SOF, so DPI (if any) is already captured
            }

            if (marker == 0xE0 && segmentLength >= 14 && offset + 2 + 2 + 12 <= content.Length)
            {
                // APP0/JFIF: identifier(5) version(2) units(1) Xdensity(2) Ydensity(2) ...
                var identifier = content.Slice(offset + 4, 5);
                if (identifier.SequenceEqual("JFIF\0"u8))
                {
                    var units = content[offset + 4 + 5 + 2];
                    var xDensity = BinaryPrimitives.ReadUInt16BigEndian(content.Slice(offset + 4 + 5 + 3, 2));
                    dpi = units switch
                    {
                        1 => xDensity, // dots per inch
                        2 => xDensity * 2.54, // dots per cm
                        _ => null,
                    };
                }
            }

            if (segmentLength < 2)
            {
                break;
            }

            offset += 2 + segmentLength;
        }

        if (width is null || height is null || width <= 0 || height <= 0)
        {
            return false;
        }

        result = new Inspected(PhotoFormat.Jpeg, width.Value, height.Value, dpi);
        return true;
    }
}
