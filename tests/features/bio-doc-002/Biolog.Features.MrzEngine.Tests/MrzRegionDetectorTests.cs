using Biolog.Features.MrzEngine.Model;
using Biolog.Features.MrzEngine.Tests.Fixtures;
using Xunit;

namespace Biolog.Features.MrzEngine.Tests;

/// <summary>Developer tests for BIO-DOC-002-AC01 ("MRZ region can be detected from
/// synthetic/reference images").</summary>
public sealed class MrzRegionDetectorTests
{
    private const string Td3Line1 = "P<UTOERIKSSON<<ANNA<MARIA<<<<<<<<<<<<<<<<<<<";
    private const string Td3Line2 = "L898902C36UTO7408122F1204159ZE184226B<<<<<10";

    [Fact]
    public void AC01_detects_a_td3_block_placed_at_the_bottom_of_a_page()
    {
        var canvas = new SyntheticRasterCanvas(width: 270, height: 60);
        canvas.DrawMrzLines([Td3Line1, Td3Line2], left: 3, top: 40);

        var result = MrzRegionDetector.Detect(canvas.ToRaster());

        Assert.True(result.Found);
        Assert.Equal(2, result.Lines.Count);
        Assert.Equal(44, result.Lines[0].LineLength);
        Assert.Equal(40, result.Lines[0].Top);
        Assert.Equal(40 + MrzGlyphFont.CellPitchY, result.Lines[1].Top);
    }

    [Fact]
    public void AC01_skips_a_non_text_ink_block_that_does_not_share_the_glyph_cell_signature()
    {
        // A "photo" - a solid rectangle of a different height/pitch entirely - must not be
        // mistaken for a text line just because it carries ink.
        var canvas = new SyntheticRasterCanvas(width: 270, height: 90);
        canvas.FillRect(left: 3, top: 5, width: 60, height: 55, ink: true); // rows [5, 60), well clear of the text block below
        canvas.DrawMrzLines([Td3Line1, Td3Line2], left: 3, top: 70);

        var result = MrzRegionDetector.Detect(canvas.ToRaster());

        Assert.True(result.Found);
        Assert.Equal(70, result.Lines[0].Top);
    }

    [Fact]
    public void AC01_reports_not_found_for_a_completely_blank_page()
    {
        var raster = DocumentRaster.Blank(width: 270, height: 60);

        var result = MrzRegionDetector.Detect(raster);

        Assert.False(result.Found);
        Assert.Equal("mrz_region_not_detected", result.RejectionReason);
    }

    [Fact]
    public void AC01_reports_not_found_for_page_noise_that_does_not_match_any_registered_line_length()
    {
        var canvas = new SyntheticRasterCanvas(width: 270, height: 30);
        // 10 glyph cells - not 30, 36 or 44, so no registered format's geometry matches.
        canvas.DrawMrzLines(["ABCDEFGHIJ"], left: 3, top: 5);

        var result = MrzRegionDetector.Detect(canvas.ToRaster());

        Assert.False(result.Found);
        Assert.Equal("mrz_region_not_detected", result.RejectionReason);
    }

    [Fact]
    public void AC01_declines_to_guess_between_two_equally_valid_mrz_shaped_blocks()
    {
        var canvas = new SyntheticRasterCanvas(width: 270, height: 80);
        canvas.DrawMrzLines([Td3Line1, Td3Line2], left: 3, top: 10);
        canvas.DrawMrzLines([Td3Line1, Td3Line2], left: 3, top: 50);

        var result = MrzRegionDetector.Detect(canvas.ToRaster());

        Assert.False(result.Found);
        Assert.Equal("multiple_mrz_candidates", result.RejectionReason);
    }

    [Fact]
    public void AC01_detects_a_td1_block_three_lines_of_thirty()
    {
        var (line1, line2, line3) = MrzSampleData.BuildTd1();
        var canvas = new SyntheticRasterCanvas(width: 200, height: 50);
        canvas.DrawMrzLines([line1, line2, line3], left: 2, top: 20);

        var result = MrzRegionDetector.Detect(canvas.ToRaster());

        Assert.True(result.Found);
        Assert.Equal(3, result.Lines.Count);
        Assert.All(result.Lines, line => Assert.Equal(30, line.LineLength));
    }

    [Fact]
    public void the_td3_worked_example_constants_are_exactly_44_characters_each()
    {
        // Guards against a hand-typed literal drifting off-length - every other TD3 test in this
        // suite depends on these two constants being exactly right.
        Assert.Equal(44, Td3Line1.Length);
        Assert.Equal(44, Td3Line2.Length);
    }
}
