using Biolog.Features.Photography.Domain;
using Xunit;

namespace Biolog.Features.Photography.Tests;

public sealed class PhotoRasterInspectorTests
{
    [Fact]
    public void Detects_a_png_and_reads_its_real_dimensions()
    {
        var result = PhotoRasterInspector.Inspect(SyntheticImages.Png(300, 200));

        Assert.NotNull(result);
        Assert.Equal(PhotoFormat.Png, result!.Value.Format);
        Assert.Equal(300, result.Value.WidthPx);
        Assert.Equal(200, result.Value.HeightPx);
    }

    [Fact]
    public void Detects_a_jpeg_and_reads_its_real_dimensions()
    {
        var result = PhotoRasterInspector.Inspect(SyntheticImages.Jpeg(640, 480));

        Assert.NotNull(result);
        Assert.Equal(PhotoFormat.Jpeg, result!.Value.Format);
        Assert.Equal(640, result.Value.WidthPx);
        Assert.Equal(480, result.Value.HeightPx);
    }

    [Fact]
    public void Rejects_content_that_matches_neither_supported_format()
    {
        var result = PhotoRasterInspector.Inspect([0x00, 0x01, 0x02, 0x03, 0x04]);
        Assert.Null(result);
    }

    [Fact]
    public void Never_trusts_a_png_extension_on_content_that_is_not_actually_a_png()
    {
        // The defense this class exists for: format is read from the bytes, never from a
        // caller-supplied claim (a filename or content-type header, handled one layer up).
        var fakeContent = "not a real image, just some bytes"u8.ToArray();
        Assert.Null(PhotoRasterInspector.Inspect(fakeContent));
    }

    [Fact]
    public void Rejects_a_truncated_png_missing_its_ihdr_chunk()
    {
        var truncated = SyntheticImages.Png(100, 100)[..10];
        Assert.Null(PhotoRasterInspector.Inspect(truncated));
    }

    [Fact]
    public void Rejects_a_truncated_jpeg_missing_its_sof_marker()
    {
        byte[] truncated = [0xFF, 0xD8, 0xFF, 0xD9]; // SOI immediately followed by EOI, no SOF
        Assert.Null(PhotoRasterInspector.Inspect(truncated));
    }
}
