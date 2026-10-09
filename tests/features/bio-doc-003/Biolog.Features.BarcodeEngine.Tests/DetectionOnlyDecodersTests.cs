using Biolog.Features.BarcodeEngine.Model;
using Biolog.Features.BarcodeEngine.Tests.Fixtures;
using Xunit;

namespace Biolog.Features.BarcodeEngine.Tests;

/// <summary>Developer tests for BIO-DOC-003-AC01's five detection-only symbologies: each is
/// found via a real, symbology-distinguishing structural signature and reports a bounding box,
/// but explicitly does not decode a payload (never a fabricated one, per AC03/AC04).</summary>
public sealed class DetectionOnlyDecodersTests
{
    [Fact]
    public void Qr_AC01_detects_three_finder_patterns_arranged_as_corners()
    {
        var canvas = new SyntheticBarcodeCanvas(width: 120, height: 120);
        canvas.DrawQrFinderPattern(left: 10, top: 10);
        canvas.DrawQrFinderPattern(left: 80, top: 10);
        canvas.DrawQrFinderPattern(left: 10, top: 80);

        var attempt = new QrSignatureDecoder().TryDecode(canvas.ToRaster());

        Assert.True(attempt.Detected);
        Assert.False(attempt.Decoded);
        Assert.Equal(BarcodeSymbology.Qr, attempt.Symbology);
        Assert.NotNull(attempt.UndecodedReason);
        Assert.NotNull(attempt.BoundingBox);
    }

    [Fact]
    public void Qr_AC01_two_finder_patterns_alone_are_not_enough_never_a_guess()
    {
        var canvas = new SyntheticBarcodeCanvas(width: 120, height: 120);
        canvas.DrawQrFinderPattern(left: 10, top: 10);
        canvas.DrawQrFinderPattern(left: 80, top: 10);

        var attempt = new QrSignatureDecoder().TryDecode(canvas.ToRaster());

        Assert.False(attempt.Detected);
    }

    [Fact]
    public void DataMatrix_AC01_detects_the_solid_l_border_and_alternating_clock_track()
    {
        var canvas = new SyntheticBarcodeCanvas(width: 100, height: 100);
        canvas.DrawDataMatrixLFinder(left: 10, top: 10, modules: 12);

        var attempt = new DataMatrixSignatureDecoder().TryDecode(canvas.ToRaster());

        Assert.True(attempt.Detected);
        Assert.False(attempt.Decoded);
        Assert.Equal(BarcodeSymbology.DataMatrix, attempt.Symbology);
    }

    [Fact]
    public void DataMatrix_AC01_a_solid_block_with_no_clock_track_is_not_detected()
    {
        var canvas = new SyntheticBarcodeCanvas(width: 100, height: 100);
        canvas.FillRect(10, 10, 48, 48, ink: true); // solid square - no alternating track at all

        var attempt = new DataMatrixSignatureDecoder().TryDecode(canvas.ToRaster());

        Assert.False(attempt.Detected);
    }

    [Fact]
    public void Aztec_AC01_detects_a_centered_alternating_bullseye()
    {
        var canvas = new SyntheticBarcodeCanvas(width: 100, height: 100);
        canvas.DrawAztecBullseye(centerX: 50, centerY: 50, rings: 6);

        var attempt = new AztecSignatureDecoder().TryDecode(canvas.ToRaster());

        Assert.True(attempt.Detected);
        Assert.False(attempt.Decoded);
        Assert.Equal(BarcodeSymbology.Aztec, attempt.Symbology);
    }

    [Fact]
    public void Aztec_AC01_a_solid_disc_with_no_alternation_is_not_detected()
    {
        var canvas = new SyntheticBarcodeCanvas(width: 100, height: 100);
        canvas.FillRect(30, 30, 40, 40, ink: true); // solid, no rings

        var attempt = new AztecSignatureDecoder().TryDecode(canvas.ToRaster());

        Assert.False(attempt.Detected);
    }

    [Fact]
    public void Pdf417_AC01_detects_several_stacked_dense_rows()
    {
        var canvas = new SyntheticBarcodeCanvas(width: 200, height: 150);
        canvas.DrawStackedRows(rowCount: 5, left: 10, top: 10, rowWidthPx: 160, rowHeightPx: 16, rowGapPx: 4);

        var attempt = new Pdf417SignatureDecoder().TryDecode(canvas.ToRaster());

        Assert.True(attempt.Detected);
        Assert.False(attempt.Decoded);
        Assert.Equal(BarcodeSymbology.Pdf417, attempt.Symbology);
    }

    [Fact]
    public void Pdf417_AC01_a_single_row_is_not_enough_never_a_guess()
    {
        var canvas = new SyntheticBarcodeCanvas(width: 200, height: 150);
        canvas.DrawStackedRows(rowCount: 1, left: 10, top: 10, rowWidthPx: 160, rowHeightPx: 16, rowGapPx: 4);

        var attempt = new Pdf417SignatureDecoder().TryDecode(canvas.ToRaster());

        Assert.False(attempt.Detected);
    }

    [Fact]
    public void Code128_AC01_detects_a_row_with_four_distinct_module_widths()
    {
        var canvas = new SyntheticBarcodeCanvas(width: 200, height: 40);
        // Bars of module widths 1,2,3,4 interleaved with equal-width spaces - deliberately not a
        // valid Code 39 pattern (which only ever has exactly two distinct widths).
        canvas.DrawVariableWidthRow([1, 1, 2, 2, 3, 3, 4, 4, 1, 2, 3, 4, 1, 1], left: 10, top: 5, heightPx: 30);

        var attempt = new Code128SignatureDecoder().TryDecode(canvas.ToRaster());

        Assert.True(attempt.Detected);
        Assert.False(attempt.Decoded);
        Assert.Equal(BarcodeSymbology.Code128, attempt.Symbology);
    }

    [Fact]
    public void Code128_AC01_a_genuine_code39_symbol_is_not_misidentified_as_code128()
    {
        var canvas = new SyntheticBarcodeCanvas(width: 300, height: 60);
        canvas.DrawCode39("XYZ", left: 20, top: 15);

        var attempt = new Code128SignatureDecoder().TryDecode(canvas.ToRaster());

        // Code 39 only ever has two distinct module widths - Code128's >=3-distinct-widths
        // signature must not fire on it.
        Assert.False(attempt.Detected);
    }

    [Fact]
    public void Code39_AC01_a_variable_width_code128_style_row_is_not_misidentified_as_code39()
    {
        var canvas = new SyntheticBarcodeCanvas(width: 200, height: 40);
        canvas.DrawVariableWidthRow([1, 1, 2, 2, 3, 3, 4, 4, 1, 2, 3, 4, 1, 1], left: 10, top: 5, heightPx: Code39Decoder.BarHeightPx);

        var attempt = new Code39Decoder().TryDecode(canvas.ToRaster());

        Assert.False(attempt.Decoded);
    }
}
