using Biolog.Features.MrzEngine.Model;
using Biolog.Features.MrzEngine.Tests.Fixtures;
using Xunit;

namespace Biolog.Features.MrzEngine.Tests;

/// <summary>Developer tests for BIO-DOC-002-AC05 ("low-confidence/invalid results require review
/// rather than guessed correction") exercised through the full <see cref="MrzReader.Read"/>
/// pipeline.</summary>
public sealed class MrzReaderTests
{
    [Fact]
    public void AC05_a_page_with_no_mrz_at_all_requires_review_and_produces_no_fields()
    {
        var raster = DocumentRaster.Blank(width: 270, height: 60);

        var outcome = MrzReader.Read(raster);

        Assert.False(outcome.Detected);
        Assert.Null(outcome.Format);
        Assert.Null(outcome.Region);
        Assert.Empty(outcome.Fields);
        Assert.True(outcome.RequiresManualReview);
        Assert.Contains("mrz_region_not_detected", outcome.ReviewReasons);
    }

    [Fact]
    public void AC05_a_low_confidence_character_is_flagged_for_review_without_changing_the_raw_value_read()
    {
        const int left = 3;
        const int top = 15;
        var canvas = new SyntheticRasterCanvas(width: 270, height: 40);
        canvas.DrawMrzLines([MrzSampleData.Td3Line1, MrzSampleData.Td3Line2], left, top);

        // Line 2, column 0 is 'L' - the first character of the document number. Overwrite just
        // that one cell with a noisy-but-still-recognized-as-'L' bitmap.
        var noisyL = GlyphCorruption.LowerConfidenceButKeepCharacter('L', flipCount: 6);
        var cellLeft = left;
        var cellTop = top + MrzGlyphFont.CellPitchY; // line index 1
        canvas.DrawBitmap(noisyL, cellLeft, cellTop);

        var outcome = MrzReader.Read(canvas.ToRaster());

        var documentNumber = outcome.Fields.Single(f => f.FieldName == "document_number");
        Assert.Equal("L898902C3", documentNumber.RawValue); // recognized character unchanged
        Assert.True(documentNumber.Confidence < MrzParser.MinAcceptableConfidence);

        Assert.True(outcome.RequiresManualReview);
        Assert.Contains("low_confidence:document_number", outcome.ReviewReasons);

        // The check digit itself was still computed correctly - low confidence and a check-digit
        // mismatch are reported as distinct, independent signals (AC03 vs AC05), not conflated.
        Assert.True(documentNumber.CheckDigitValid);
    }

    [Fact]
    public void AC05_review_reasons_never_repeat_the_same_reason_string_per_field()
    {
        var canvas = new SyntheticRasterCanvas(width: 270, height: 40);
        canvas.DrawMrzLines([MrzSampleData.Td3Line1, MrzSampleData.Td3Line2], left: 3, top: 15);

        var outcome = MrzReader.Read(canvas.ToRaster());

        Assert.Equal(outcome.ReviewReasons.Count, outcome.ReviewReasons.Distinct().Count());
    }
}
