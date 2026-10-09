// Developer tests for BIO-CRD-001-AC03. SYNTHETIC: tiny in-memory fixture images only.
using Biolog.Api.Features.BrandAssets;
using Xunit;

namespace Biolog.UnitTests.Features.BrandAssets;

public sealed class RasterImageInspectorTests
{
    [Fact]
    public void TryReadPng_reads_dimensions_and_alpha_from_a_real_png()
    {
        var png = SyntheticImages.Png(width: 300, height: 150, withAlpha: true);
        var header = RasterImageInspector.TryReadPng(png);

        Assert.NotNull(header);
        Assert.Equal(300, header!.Value.Width);
        Assert.Equal(150, header.Value.Height);
        Assert.True(header.Value.HasAlpha);
    }

    [Fact]
    public void TryReadPng_reads_native_dpi_from_pHYs_chunk()
    {
        var png = SyntheticImages.Png(width: 100, height: 100, withAlpha: false, pixelsPerMetre: 11811); // ~300 DPI
        var header = RasterImageInspector.TryReadPng(png);

        Assert.NotNull(header!.Value.NativeDpi);
        Assert.InRange(header.Value.NativeDpi!.Value, 299, 301);
    }

    [Fact]
    public void TryReadPng_returns_null_for_non_png_bytes()
    {
        Assert.Null(RasterImageInspector.TryReadPng([0x00, 0x01, 0x02, 0x03]));
    }

    [Fact]
    public void TryReadJpeg_reads_dimensions_from_sof0()
    {
        var jpeg = SyntheticImages.Jpeg(width: 640, height: 480);
        var header = RasterImageInspector.TryReadJpeg(jpeg);

        Assert.NotNull(header);
        Assert.Equal(640, header!.Value.Width);
        Assert.Equal(480, header.Value.Height);
        Assert.False(header.Value.HasAlpha);
    }

    [Fact]
    public void TryReadJpeg_reads_native_dpi_from_jfif_app0()
    {
        var jpeg = SyntheticImages.Jpeg(width: 100, height: 100, densityUnits: 1, xDensity: 300);
        var header = RasterImageInspector.TryReadJpeg(jpeg);

        Assert.Equal(300, header!.Value.NativeDpi);
    }

    [Theory]
    [InlineData(72, true)] // below the 150 DPI floor
    [InlineData(150, false)] // exactly at the floor is not "below" it
    [InlineData(300, false)]
    public void Assess_flags_low_resolution_against_intended_print_size(double intendedDpi, bool expectWarning)
    {
        // A 300px-wide image at intendedDpi requires intendedWidthMm = 300 / intendedDpi inches, in mm.
        var widthPx = 300;
        var widthMm = widthPx / intendedDpi * 25.4;
        var header = new RasterImageInspector.RasterHeader(widthPx, widthPx, false, null);

        var assessment = RasterImageInspector.Assess(header, widthMm);

        Assert.Equal(expectWarning, assessment.LowResolutionWarning);
        Assert.NotNull(assessment.EffectiveDpi);
        Assert.InRange(assessment.EffectiveDpi!.Value, intendedDpi - 0.5, intendedDpi + 0.5);
    }

    [Fact]
    public void Assess_falls_back_to_native_dpi_when_no_intended_size_given()
    {
        var header = new RasterImageInspector.RasterHeader(100, 100, false, NativeDpi: 72);
        var assessment = RasterImageInspector.Assess(header, intendedWidthMm: null);

        Assert.Equal(72, assessment.EffectiveDpi);
        Assert.True(assessment.LowResolutionWarning);
    }

    [Fact]
    public void Assess_has_no_dpi_warning_when_neither_intended_size_nor_native_dpi_is_known()
    {
        var header = new RasterImageInspector.RasterHeader(100, 100, false, NativeDpi: null);
        var assessment = RasterImageInspector.Assess(header, intendedWidthMm: null);

        Assert.Null(assessment.EffectiveDpi);
        Assert.False(assessment.LowResolutionWarning);
        Assert.Null(assessment.WarningMessage);
    }
}
