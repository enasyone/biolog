// Test fixture builders. SYNTHETIC: no real artwork, minimal valid headers only.
using System.Buffers.Binary;
using System.Text;

namespace Biolog.UnitTests.Features.BrandAssets;

public static class SyntheticImages
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static byte[] Png(int width, int height, bool withAlpha, uint? pixelsPerMetre = null)
    {
        using var stream = new MemoryStream();
        stream.Write(PngSignature);

        var ihdr = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(0, 4), (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4, 4), (uint)height);
        ihdr[8] = 8; // bit depth
        ihdr[9] = (byte)(withAlpha ? 6 : 2); // colour type: 6 = truecolor+alpha, 2 = truecolor
        WriteChunk(stream, "IHDR", ihdr);

        if (pixelsPerMetre is { } ppu)
        {
            var phys = new byte[9];
            BinaryPrimitives.WriteUInt32BigEndian(phys.AsSpan(0, 4), ppu);
            BinaryPrimitives.WriteUInt32BigEndian(phys.AsSpan(4, 4), ppu);
            phys[8] = 1; // unit: metre
            WriteChunk(stream, "pHYs", phys);
        }

        WriteChunk(stream, "IDAT", []);
        WriteChunk(stream, "IEND", []);
        return stream.ToArray();
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        var length = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length);
        stream.Write(length);
        var typeBytes = Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes);
        stream.Write(data);
        var crc = Crc32(typeBytes, data);
        var crcBytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        stream.Write(crcBytes);
    }

    private static uint Crc32(byte[] type, byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (var b in type.Concat(data))
        {
            crc ^= b;
            for (var i = 0; i < 8; i++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
            }
        }
        return crc ^ 0xFFFFFFFF;
    }

    public static byte[] Jpeg(int width, int height, byte densityUnits = 0, ushort xDensity = 1)
    {
        using var stream = new MemoryStream();
        stream.Write([0xFF, 0xD8]); // SOI

        var jfif = new List<byte>();
        jfif.AddRange("JFIF\0"u8.ToArray());
        jfif.AddRange([0x01, 0x01]); // version 1.1
        jfif.Add(densityUnits);
        WriteUInt16(jfif, xDensity);
        WriteUInt16(jfif, xDensity);
        jfif.AddRange([0x00, 0x00]); // no thumbnail
        WriteSegment(stream, 0xE0, jfif.ToArray());

        var sof0 = new List<byte> { 8 }; // precision
        WriteUInt16(sof0, (ushort)height);
        WriteUInt16(sof0, (ushort)width);
        sof0.Add(1); // 1 component
        sof0.AddRange([0x01, 0x11, 0x00]); // component id, sampling factors, quant table id
        WriteSegment(stream, 0xC0, sof0.ToArray());

        stream.Write([0xFF, 0xD9]); // EOI
        return stream.ToArray();
    }

    private static void WriteSegment(Stream stream, byte marker, byte[] data)
    {
        stream.Write([0xFF, marker]);
        var length = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(length, (ushort)(data.Length + 2));
        stream.Write(length);
        stream.Write(data);
    }

    private static void WriteUInt16(List<byte> buffer, ushort value)
    {
        var bytes = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
        buffer.AddRange(bytes);
    }

    public const string ValidSvg = """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10"><circle cx="5" cy="5" r="4" fill="navy"/></svg>""";

    public const string SvgWithScriptTag = """<svg xmlns="http://www.w3.org/2000/svg"><script>alert(1)</script></svg>""";

    public const string SvgWithEventHandler = """<svg xmlns="http://www.w3.org/2000/svg" onload="alert(1)"><rect width="1" height="1"/></svg>""";

    public const string SvgWithJavascriptUri = """<svg xmlns="http://www.w3.org/2000/svg"><a href="javascript:alert(1)"><rect width="1" height="1"/></a></svg>""";

    public const string SvgWithForeignObject = """<svg xmlns="http://www.w3.org/2000/svg"><foreignObject><body xmlns="http://www.w3.org/1999/xhtml">hi</body></foreignObject></svg>""";

    public const string SvgWithXxeDoctype = "<?xml version=\"1.0\"?><!DOCTYPE svg [<!ENTITY xxe SYSTEM \"file:///etc/passwd\">]><svg xmlns=\"http://www.w3.org/2000/svg\">&xxe;</svg>";

    public static byte[] EicarInAPngContainer()
    {
        var png = Png(1, 1, withAlpha: false);
        return [.. png, .. Encoding.ASCII.GetBytes(BrandAssetTestConstants.EicarTestString)];
    }
}

public static class BrandAssetTestConstants
{
    public const string EicarTestString = "X5O!P%@AP[4\\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*";
}
