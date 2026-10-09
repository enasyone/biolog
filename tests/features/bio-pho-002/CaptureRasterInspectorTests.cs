using Biolog.Features.PhotoBooth.Domain;
using Xunit;

namespace Biolog.Features.PhotoBooth.Tests;

public sealed class CaptureRasterInspectorTests
{
    [Fact]
    public void Detects_a_png_and_reads_its_real_dimensions()
    {
        var result = CaptureRasterInspector.Inspect(SyntheticImages.Png(1050, 1350));

        Assert.NotNull(result);
        Assert.Equal(CaptureFormat.Png, result!.Format);
        Assert.Equal(1050, result.WidthPx);
        Assert.Equal(1350, result.HeightPx);
        Assert.Null(result.NativeDpi);
    }

    [Fact]
    public void Reads_the_embedded_png_dpi_from_the_pHYs_chunk()
    {
        var result = CaptureRasterInspector.Inspect(SyntheticImages.Png(1050, 1350, dpi: 300));

        Assert.NotNull(result);
        Assert.NotNull(result!.NativeDpi);
        Assert.InRange(result.NativeDpi!.Value, 299, 301);
    }

    [Fact]
    public void Detects_a_jpeg_and_reads_its_real_dimensions()
    {
        var result = CaptureRasterInspector.Inspect(SyntheticImages.Jpeg(1050, 1350));

        Assert.NotNull(result);
        Assert.Equal(CaptureFormat.Jpeg, result!.Format);
        Assert.Equal(1050, result.WidthPx);
        Assert.Equal(1350, result.HeightPx);
    }

    [Fact]
    public void Reads_the_embedded_jpeg_dpi_from_the_jfif_app0_segment()
    {
        var result = CaptureRasterInspector.Inspect(SyntheticImages.Jpeg(1050, 1350, dpi: 300));

        Assert.NotNull(result);
        Assert.Equal(300, result!.NativeDpi);
    }

    [Fact]
    public void Rejects_content_that_matches_neither_supported_format()
    {
        Assert.Null(CaptureRasterInspector.Inspect([0x00, 0x01, 0x02, 0x03, 0x04]));
    }

    [Fact]
    public void Never_trusts_a_claimed_format_that_the_bytes_do_not_actually_match()
    {
        var fakeContent = "not a real image, just some bytes"u8.ToArray();
        Assert.Null(CaptureRasterInspector.Inspect(fakeContent));
    }
}
