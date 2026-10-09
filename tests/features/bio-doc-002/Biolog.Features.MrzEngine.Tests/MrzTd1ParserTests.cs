using Biolog.Features.MrzEngine.Model;
using Biolog.Features.MrzEngine.Tests.Fixtures;
using Xunit;

namespace Biolog.Features.MrzEngine.Tests;

/// <summary>Developer tests for BIO-DOC-002-AC02 ("TD1 ... [is] parsed") - the 3-line/30-column
/// national-ID-card layout, whose composite check digit spans both line 1 and line 2 (not
/// contiguous within a single line, unlike TD2/TD3).</summary>
public sealed class MrzTd1ParserTests
{
    [Fact]
    public void AC02_parses_a_valid_td1_sample_across_all_three_lines_with_no_review_needed()
    {
        var (line1, line2, line3) = MrzSampleData.BuildTd1();
        var canvas = new SyntheticRasterCanvas(width: 190, height: 50);
        canvas.DrawMrzLines([line1, line2, line3], left: 2, top: 15);

        var outcome = MrzReader.Read(canvas.ToRaster());

        Assert.True(outcome.Detected);
        Assert.Equal(MrzFormatKind.Td1, outcome.Format);
        Assert.False(outcome.RequiresManualReview);
        Assert.Empty(outcome.ReviewReasons);

        var byName = outcome.Fields.ToDictionary(f => f.FieldName);
        Assert.Equal("D23145890", byName["document_number"].RawValue);
        Assert.Equal("740812", byName["date_of_birth"].RawValue);
        Assert.Equal("120415", byName["expiry"].RawValue);
        Assert.Equal("UTO", byName["nationality"].RawValue);
        Assert.Equal("ERIKSSON, ANNA MARIA", byName["name"].NormalizedValue);

        Assert.True(outcome.CompositeCheckDigitApplicable);
        Assert.True(outcome.CompositeCheckDigitValid);
    }

    [Fact]
    public void AC02_two_separately_positioned_optional_data_fields_get_distinct_field_names()
    {
        var (line1, line2, line3) = MrzSampleData.BuildTd1(optionalData1: "AB", optionalData2: "CD");
        var canvas = new SyntheticRasterCanvas(width: 190, height: 50);
        canvas.DrawMrzLines([line1, line2, line3], left: 2, top: 15);

        var outcome = MrzReader.Read(canvas.ToRaster());
        var byName = outcome.Fields.ToDictionary(f => f.FieldName);

        Assert.Equal("AB".PadRight(15, '<'), byName["optional_data_1"].RawValue);
        Assert.Equal("CD".PadRight(11, '<'), byName["optional_data_2"].RawValue);
    }

    [Fact]
    public void AC03_a_wrong_composite_check_digit_is_reported_even_when_every_field_digit_matches()
    {
        var (line1, line2, line3) = MrzSampleData.BuildTd1();
        var corruptedLine2 = ReplaceCompositeCheckDigit(line2);
        var canvas = new SyntheticRasterCanvas(width: 190, height: 50);
        canvas.DrawMrzLines([line1, corruptedLine2, line3], left: 2, top: 15);

        var outcome = MrzReader.Read(canvas.ToRaster());

        Assert.All(outcome.Fields.Where(f => f.HasCheckDigit), f => Assert.True(f.CheckDigitValid));
        Assert.False(outcome.CompositeCheckDigitValid);
        Assert.True(outcome.RequiresManualReview);
        Assert.Contains("composite_check_digit_mismatch", outcome.ReviewReasons);
    }

    private static string ReplaceCompositeCheckDigit(string line2)
    {
        var chars = line2.ToCharArray();
        chars[29] = chars[29] == '0' ? '1' : '0'; // TD1 composite check digit is the last column
        return new string(chars);
    }
}
