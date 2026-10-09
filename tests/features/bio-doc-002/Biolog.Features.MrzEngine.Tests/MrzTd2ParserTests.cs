using Biolog.Features.MrzEngine.Model;
using Biolog.Features.MrzEngine.Tests.Fixtures;
using Xunit;

namespace Biolog.Features.MrzEngine.Tests;

/// <summary>Developer tests for BIO-DOC-002-AC02 ("TD1/TD2 ... are parsed") - TD2's 2-line/36-column
/// layout, built self-consistently by <see cref="MrzSampleData.BuildTd2"/> rather than a
/// hand-counted literal.</summary>
public sealed class MrzTd2ParserTests
{
    [Fact]
    public void AC02_parses_a_valid_td2_sample_with_no_review_needed()
    {
        var (line1, line2) = MrzSampleData.BuildTd2();
        var canvas = new SyntheticRasterCanvas(width: 220, height: 40);
        canvas.DrawMrzLines([line1, line2], left: 2, top: 15);

        var outcome = MrzReader.Read(canvas.ToRaster());

        Assert.True(outcome.Detected);
        Assert.Equal(MrzFormatKind.Td2, outcome.Format);
        Assert.False(outcome.RequiresManualReview);
        Assert.Empty(outcome.ReviewReasons);

        var byName = outcome.Fields.ToDictionary(f => f.FieldName);
        Assert.Equal("D23145890", byName["document_number"].RawValue);
        Assert.Equal("UTO", byName["nationality"].RawValue);
        Assert.Equal("ERIKSSON, ANNA MARIA", byName["name"].NormalizedValue);

        Assert.True(outcome.CompositeCheckDigitApplicable);
        Assert.True(outcome.CompositeCheckDigitValid);
    }

    [Fact]
    public void AC03_flags_a_wrong_expiry_check_digit_without_altering_the_raw_expiry_value()
    {
        var (line1, validLine2) = MrzSampleData.BuildTd2(expiry: "301231");
        var wrongExpiryDigit = MrzCheckDigit.Compute("301231") == 9 ? '8' : '9';
        var corruptedLine2 = ReplaceExpiryCheckDigit(validLine2, wrongExpiryDigit);

        var canvas = new SyntheticRasterCanvas(width: 220, height: 40);
        canvas.DrawMrzLines([line1, corruptedLine2], left: 2, top: 15);

        var outcome = MrzReader.Read(canvas.ToRaster());

        var expiry = outcome.Fields.Single(f => f.FieldName == "expiry");
        Assert.Equal("301231", expiry.RawValue);
        Assert.False(expiry.CheckDigitValid);
        Assert.True(outcome.RequiresManualReview);
        Assert.Contains("check_digit_mismatch:expiry", outcome.ReviewReasons);
    }

    private static string ReplaceExpiryCheckDigit(string line2, char digit)
    {
        // expiry check digit sits at TD2 line-2 column 27 (0-based).
        var chars = line2.ToCharArray();
        chars[27] = digit;
        return new string(chars);
    }
}
