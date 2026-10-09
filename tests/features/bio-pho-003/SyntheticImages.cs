using System.Buffers.Binary;
using System.Text;

namespace Biolog.Features.PhotoQuality.Tests;

/// <summary>Minimal, valid, SYNTHETIC PNG/JPEG byte builders, optionally embedding a native DPI - no real photo or biometric data anywhere in this test project.</summary>
public static class SyntheticImages
{
    public static byte[] Png(int width, int height, double? dpi = null)
    {
        using var stream = new MemoryStream();
        stream.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var ihdr = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(0, 4), (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4, 4), (uint)height);
        ihdr[8] = 8;
        ihdr[9] = 2; // RGB, no alpha
        WriteChunk(stream, "IHDR", ihdr);

        if (dpi is { } value)
        {
            var pixelsPerMetre = (uint)Math.Round(value / 0.0254);
            var phys = new byte[9];
            BinaryPrimitives.WriteUInt32BigEndian(phys.AsSpan(0, 4), pixelsPerMetre);
            BinaryPrimitives.WriteUInt32BigEndian(phys.AsSpan(4, 4), pixelsPerMetre);
            phys[8] = 1; // metre
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
        uint crc = 0xFFFFFFFF;
        foreach (var b in typeBytes.Concat(data))
        {
            crc ^= b;
            for (var i = 0; i < 8; i++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
        }
        var crcBytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc ^ 0xFFFFFFFF);
        stream.Write(crcBytes);
    }
}
