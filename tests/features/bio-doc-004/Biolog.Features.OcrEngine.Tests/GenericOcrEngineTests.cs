using System.Reflection;
using Biolog.Features.OcrEngine.Model;
using Biolog.Features.OcrEngine.Tests.Fixtures;
using Xunit;

namespace Biolog.Features.OcrEngine.Tests;

/// <summary>Developer tests for BIO-DOC-004-AC02 ("generic OCR returns text blocks without
/// pretending field semantics").</summary>
public sealed class GenericOcrEngineTests
{
    private static readonly IOcrLanguagePack English = new EnglishLanguagePack();

    [Fact]
    public void AC02_reads_an_arbitrary_line_with_no_template_and_no_assigned_field_type()
    {
        var canvas = new SyntheticOcrCanvas(width: 200, height: 40);
        canvas.DrawText("HELLO WORLD", left: 10, top: 10);

        var result = GenericOcrEngine.Read(canvas.ToRaster(), English);

        var block = Assert.Single(result.Blocks);
        Assert.Equal("HELLO WORLD", block.RawText);
        Assert.Equal(1.0, block.Confidence);
    }

    [Fact]
    public void AC02_two_independent_lines_anywhere_on_the_page_are_both_detected()
    {
        var canvas = new SyntheticOcrCanvas(width: 300, height: 200);
        canvas.DrawText("FIRST LINE", left: 10, top: 10);
        canvas.DrawText("A SECOND ONE HERE", left: 20, top: 150);

        var result = GenericOcrEngine.Read(canvas.ToRaster(), English);

        Assert.Equal(2, result.Blocks.Count);
        Assert.Contains(result.Blocks, b => b.RawText == "FIRST LINE");
        Assert.Contains(result.Blocks, b => b.RawText == "A SECOND ONE HERE");
    }

    [Fact]
    public void AC02_a_blank_page_produces_no_blocks()
    {
        var raster = DocumentRaster.Blank(width: 200, height: 40);

        var result = GenericOcrEngine.Read(raster, English);

        Assert.Empty(result.Blocks);
    }

    [Fact]
    public void AC02_GenericTextBlock_has_no_field_type_or_field_name_property_at_all()
    {
        var properties = typeof(GenericTextBlock).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        Assert.DoesNotContain(properties, p => p.PropertyType == typeof(OcrFieldType));
        Assert.DoesNotContain(properties, p => p.Name.Contains("FieldName", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(properties, p => p.Name.Contains("FieldType", StringComparison.OrdinalIgnoreCase));
    }
}
