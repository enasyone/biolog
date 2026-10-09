using Biolog.Features.OcrEngine.Model;
using Biolog.Features.OcrEngine.Tests.Fixtures;
using Xunit;

namespace Biolog.Features.OcrEngine.Tests;

/// <summary>Developer tests for BIO-DOC-004-AC01 (template OCR fields), AC04 (critical low-
/// confidence review) and AC05 (never a guessed correction).</summary>
public sealed class TemplateOcrEngineTests
{
    private static readonly IOcrLanguagePack English = new EnglishLanguagePack();

    [Fact]
    public void AC01_reads_field_type_raw_normalized_confidence_and_source_coordinates()
    {
        var canvas = new SyntheticOcrCanvas(width: 200, height: 40);
        canvas.DrawText("OKAFOR", left: 10, top: 10);
        var template = new OcrDocumentTemplate("test-id", [
            new OcrTemplateField("surname", OcrFieldType.Surname, Left: 10, Top: 10, CharacterCount: 6, Critical: true),
        ]);

        var result = TemplateOcrEngine.Read(canvas.ToRaster(), template, English);

        var field = Assert.Single(result.Fields);
        Assert.Equal("surname", field.FieldName);
        Assert.Equal(OcrFieldType.Surname, field.FieldType);
        Assert.Equal("OKAFOR", field.RawValue);
        Assert.Equal("OKAFOR", field.NormalizedValue);
        Assert.Equal(1.0, field.Confidence);
        Assert.True(field.BoundingBox.X >= 0 && field.BoundingBox.X < 1);
    }

    [Fact]
    public void AC01_normalizes_a_date_field_into_a_readable_grouping()
    {
        var canvas = new SyntheticOcrCanvas(width: 100, height: 40);
        canvas.DrawText("740812", left: 5, top: 10);
        var template = new OcrDocumentTemplate("dob-template", [
            new OcrTemplateField("date_of_birth", OcrFieldType.DateOfBirth, 5, 10, 6, Critical: true),
        ]);

        var result = TemplateOcrEngine.Read(canvas.ToRaster(), template, English);

        var field = Assert.Single(result.Fields);
        Assert.Equal("740812", field.RawValue);
        Assert.Equal("74-08-12", field.NormalizedValue);
    }

    [Fact]
    public void AC04_a_critical_field_below_confidence_threshold_requires_manual_review()
    {
        var canvas = new SyntheticOcrCanvas(width: 100, height: 40);
        var noisyO = GlyphCorruption.LowerConfidenceButKeepCharacter('O', flipCount: 6);
        canvas.DrawBitmap(noisyO, left: 10, top: 10);
        var template = new OcrDocumentTemplate("t", [
            new OcrTemplateField("document_number", OcrFieldType.DocumentNumber, 10, 10, 1, Critical: true),
        ]);

        var result = TemplateOcrEngine.Read(canvas.ToRaster(), template, English);

        Assert.True(result.RequiresManualReview);
        Assert.Contains("low_confidence:document_number", result.ReviewReasons);
        // The recognized character itself is unchanged - low confidence is flagged, not "fixed".
        Assert.Equal("O", result.Fields[0].RawValue);
    }

    [Fact]
    public void AC04_a_non_critical_field_below_confidence_threshold_never_gates_review()
    {
        var canvas = new SyntheticOcrCanvas(width: 100, height: 40);
        var noisyO = GlyphCorruption.LowerConfidenceButKeepCharacter('O', flipCount: 6);
        canvas.DrawBitmap(noisyO, left: 10, top: 10);
        var template = new OcrDocumentTemplate("t", [
            new OcrTemplateField("decorative_note", OcrFieldType.FreeText, 10, 10, 1, Critical: false),
        ]);

        var result = TemplateOcrEngine.Read(canvas.ToRaster(), template, English);

        Assert.False(result.RequiresManualReview);
        Assert.Empty(result.ReviewReasons);
    }

    [Fact]
    public void AC05_an_unreadable_glyph_still_reports_the_engines_best_match_never_a_blank_or_placeholder()
    {
        var raster = DocumentRaster.Blank(width: 100, height: 40); // pure noise/blank cell
        var template = new OcrDocumentTemplate("t", [
            new OcrTemplateField("field", OcrFieldType.FreeText, 10, 10, 1, Critical: true),
        ]);

        var result = TemplateOcrEngine.Read(raster, template, English);

        // A blank cell nearest-matches the space glyph exactly (confidence 1.0) - not omitted,
        // not replaced by a sentinel/placeholder value.
        Assert.Equal(" ", result.Fields[0].RawValue);
    }

    [Fact]
    public void AC01_two_fields_at_unrelated_positions_are_both_read_independently()
    {
        var canvas = new SyntheticOcrCanvas(width: 300, height: 100);
        canvas.DrawText("CHIOMA", left: 10, top: 10);
        canvas.DrawText("F", left: 200, top: 70);
        var template = new OcrDocumentTemplate("t", [
            new OcrTemplateField("given_names", OcrFieldType.GivenNames, 10, 10, 6, Critical: true),
            new OcrTemplateField("sex", OcrFieldType.Sex, 200, 70, 1, Critical: true),
        ]);

        var result = TemplateOcrEngine.Read(canvas.ToRaster(), template, English);

        Assert.Equal("CHIOMA", result.Fields[0].RawValue);
        Assert.Equal("F", result.Fields[1].RawValue);
        Assert.Equal("female", result.Fields[1].NormalizedValue);
    }
}
