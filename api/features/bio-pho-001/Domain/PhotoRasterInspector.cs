using System.Buffers.Binary;

namespace Biolog.Features.Photography.Domain;

/// <summary>
/// Dependency-free format detection and dimension reading for the two capture formats this
/// story supports (BIO-PHO-001-AC01). Format is always detected from the file's own magic
/// bytes - a caller-supplied content type or file extension is never trusted (the same
/// defense <c>BrandAssetFormatDetector</c> (BIO-CRD-001) applies to a different asset class).
///
/// <b>No pixel resampling here, or anywhere in this project - a disclosed scope boundary.</b>
/// No image-processing library (SixLabors.ImageSharp, SkiaSharp, System.Drawing) is referenced
/// anywhere in this host yet, and adding one is outside this story's own write scope without
/// further specification. This class only reads header metadata (format, pixel dimensions),
/// the same "parse the header, never decode/re-encode pixels" boundary
/// <c>RasterImageInspector</c> (BIO-CRD-001) already established for a different feature - see
/// <see cref="IPhotoDerivativeGenerator"/>'s header for how that boundary carries into
/// derivative generation.
/// </summary>
public static class PhotoRasterInspector
{
    private static readonly byte[] PngMagic = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] JpegMagic = [0xFF, 0xD8];

    /// <summary>Detects the real format from magic bytes and reads its pixel dimensions in one pass. Returns null when the content matches neither supported format.</summary>
    public static (PhotoFormat Format, int WidthPx, int HeightPx)? Inspect(ReadOnlySpan<byte> content)
    {
        if (TryReadPng(content, out var pngWidth, out var pngHeight))
        {
            return (PhotoFormat.Png, pngWidth, pngHeight);
        }

        if (TryReadJpeg(content, out var jpegWidth, out var jpegHeight))
        {
            return (PhotoFormat.Jpeg, jpegWidth, jpegHeight);
        }

        return null;
    }

    private static bool TryReadPng(ReadOnlySpan<byte> content, out int width, out int height)
    {
        width = 0;
        height = 0;

        // 8-byte signature, then the mandatory first chunk: 4-byte length, 4-byte type "IHDR",
        // then width/height as 4-byte big-endian integers (PNG spec §11.2.2).
        if (content.Length < 8 + 8 + 8 || !content[..8].SequenceEqual(PngMagic))
        {
            return false;
        }

        if (content[12] != (byte)'I' || content[13] != (byte)'H' || content[14] != (byte)'D' || content[15] != (byte)'R')
        {
            return false;
        }

        width = (int)BinaryPrimitives.ReadUInt32BigEndian(content.Slice(16, 4));
        height = (int)BinaryPrimitives.ReadUInt32BigEndian(content.Slice(20, 4));
        return width > 0 && height > 0;
    }

    private static bool TryReadJpeg(ReadOnlySpan<byte> content, out int width, out int height)
    {
        width = 0;
        height = 0;

        if (content.Length < 4 || content[0] != JpegMagic[0] || content[1] != JpegMagic[1])
        {
            return false;
        }

        var offset = 2;
        while (offset + 4 <= content.Length)
        {
            if (content[offset] != 0xFF)
            {
                return false; // not a well-formed marker stream
            }

            var marker = content[offset + 1];
            // SOFn markers that carry dimensions: 0xC0-0xC3, 0xC5-0xC7, 0xC9-0xCB, 0xCD-0xCF
            // (JPEG spec Table B.1), excluding 0xC4/0xC8/0xCC (not SOF markers).
            var isSof = marker is >= 0xC0 and <= 0xCF && marker is not 0xC4 and not 0xC8 and not 0xCC;

            if (marker == 0xD8 || marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7))
            {
                offset += 2; // markers with no length payload
                continue;
            }

            if (offset + 4 > content.Length)
            {
                return false;
            }

            var segmentLength = BinaryPrimitives.ReadUInt16BigEndian(content.Slice(offset + 2, 2));
            if (isSof)
            {
                if (offset + 2 + 2 + 5 > content.Length)
                {
                    return false;
                }

                height = BinaryPrimitives.ReadUInt16BigEndian(content.Slice(offset + 5, 2));
                width = BinaryPrimitives.ReadUInt16BigEndian(content.Slice(offset + 7, 2));
                return width > 0 && height > 0;
            }

            if (marker == 0xD9 || segmentLength < 2)
            {
                return false; // end of image or a malformed segment, before an SOF was found
            }

            offset += 2 + segmentLength;
        }

        return false;
    }
}
