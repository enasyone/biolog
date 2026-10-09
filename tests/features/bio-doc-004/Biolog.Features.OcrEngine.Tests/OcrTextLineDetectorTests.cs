using Biolog.Features.OcrEngine.Model;
using Biolog.Features.OcrEngine.Tests.Fixtures;
using Xunit;

namespace Biolog.Features.OcrEngine.Tests;

public sealed class OcrTextLineDetectorTests
{
    [Fact]
    public void detects_a_single_character_line()
    {
        var canvas = new SyntheticOcrCanvas(width: 40, height: 40);
        canvas.DrawText("A", left: 10, top: 10);

        var lines = OcrTextLineDetector.DetectLines(canvas.ToRaster());

        var line = Assert.Single(lines);
        Assert.Single(line.CellStartColumns);
    }

    [Fact]
    public void detects_a_long_line_of_arbitrary_length_not_from_a_fixed_whitelist()
    {
        var canvas = new SyntheticOcrCanvas(width: 400, height: 40);
        const string text = "THIS IS A REASONABLY LONG LINE OF TEXT";
        canvas.DrawText(text, left: 5, top: 10);

        var lines = OcrTextLineDetector.DetectLines(canvas.ToRaster());

        var line = Assert.Single(lines);
        Assert.Equal(text.Length, line.CellStartColumns.Count);
    }

    [Fact]
    public void a_non_text_solid_block_is_not_mistaken_for_a_line()
    {
        var canvas = new SyntheticOcrCanvas(width: 200, height: 60);
        canvas.FillRect(10, 5, 100, 20, ink: true); // wrong height entirely - a photo block

        var lines = OcrTextLineDetector.DetectLines(canvas.ToRaster());

        Assert.Empty(lines);
    }

    [Fact]
    public void a_blank_page_has_no_lines()
    {
        var raster = DocumentRaster.Blank(width: 200, height: 60);
        Assert.Empty(OcrTextLineDetector.DetectLines(raster));
    }
}
