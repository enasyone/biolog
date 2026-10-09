using Biolog.Features.DocumentVerify.Model;
using Xunit;

namespace Biolog.Features.DocumentVerify.Tests;

/// <summary>Developer tests for BIO-DOC-001-AC03 ("evidence records preserve raw, normalized,
/// confidence, source and bounding-box metadata").</summary>
public sealed class DocumentFieldEvidenceTests
{
    private const string TenantId = "ten_test-tenant";
    private const string SessionId = "dcs_1";
    private const string ExtractedAt = "2026-09-21T12:00:02.000Z";

    private static BoundingBox SampleBox() => BoundingBox.Create(0.1, 0.2, 0.3, 0.1);

    [Fact]
    public void AC03_preserves_raw_normalized_confidence_source_and_bounding_box_together()
    {
        var box = SampleBox();

        var field = DocumentFieldEvidence.Create(
            TenantId, SessionId, sequenceNumber: 1, sourceEvidenceSequenceNumber: 1,
            fieldName: "surname", rawValue: "OKAFOR<<CHIOMA", normalizedValue: "Okafor",
            confidence: 0.94, FieldEvidenceSource.Mrz, box, ExtractedAt, EvidenceClassification.Personal);

        Assert.Equal("OKAFOR<<CHIOMA", field.RawValue);
        Assert.Equal("Okafor", field.NormalizedValue);
        Assert.Equal(0.94, field.Confidence);
        Assert.Equal(FieldEvidenceSource.Mrz, field.Source);
        Assert.Equal(box, field.BoundingBox);
    }

    [Fact]
    public void AC03_bounding_box_is_optional_for_a_source_with_no_image_location_such_as_nfc()
    {
        var field = DocumentFieldEvidence.Create(
            TenantId, SessionId, 1, 1, "document_number", "A1234567", "A1234567",
            confidence: 1.0, FieldEvidenceSource.Nfc, boundingBox: null, ExtractedAt,
            EvidenceClassification.Personal);

        Assert.Null(field.BoundingBox);
    }

    [Fact]
    public void AC03_is_immutable_a_correction_is_an_independently_created_record_not_a_mutation()
    {
        // QA-03 (Chioma acceptance audit): every property's init accessor is now `private`, so a
        // "correction" can no longer be expressed as `original with { ... }` from outside this
        // type at all - it must go through Create again, validated the same as any other evidence.
        var original = DocumentFieldEvidence.Create(
            TenantId, SessionId, 1, 1, "given_names", "CHIOMA", "Chioma",
            0.8, FieldEvidenceSource.Ocr, null, ExtractedAt, EvidenceClassification.Personal);

        var corrected = DocumentFieldEvidence.Create(
            TenantId, SessionId, 1, 1, "given_names", "CHIOMA", "Chioma-Amara",
            0.8, FieldEvidenceSource.Manual, null, ExtractedAt, EvidenceClassification.Personal);

        Assert.Equal("Chioma", original.NormalizedValue);
        Assert.Equal(FieldEvidenceSource.Ocr, original.Source);
        Assert.Equal("Chioma-Amara", corrected.NormalizedValue);
        Assert.Equal(FieldEvidenceSource.Manual, corrected.Source);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    [InlineData(double.NaN)]
    public void rejects_a_confidence_outside_zero_to_one(double badConfidence)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DocumentFieldEvidence.Create(
            TenantId, SessionId, 1, 1, "given_names", "X", "X", badConfidence,
            FieldEvidenceSource.Ocr, null, ExtractedAt, EvidenceClassification.Personal));
    }

    [Fact]
    public void rejects_a_null_raw_or_normalized_value_but_allows_empty_string_for_a_field_not_found()
    {
        Assert.Throws<ArgumentNullException>(() => DocumentFieldEvidence.Create(
            TenantId, SessionId, 1, 1, "given_names", null!, "X", 0.5,
            FieldEvidenceSource.Ocr, null, ExtractedAt, EvidenceClassification.Personal));

        var notFound = DocumentFieldEvidence.Create(
            TenantId, SessionId, 1, 1, "middle_name", "", "", 0.0,
            FieldEvidenceSource.Ocr, null, ExtractedAt, EvidenceClassification.Personal);
        Assert.Equal("", notFound.RawValue);
    }

    // Found alongside QA-03 while closing it: see DocumentEvidenceTests's identical undefined-
    // enum tests for why Create() needed this even though the finding named only DocumentEvidence.
    [Fact]
    public void rejects_an_undefined_source_value()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DocumentFieldEvidence.Create(
            TenantId, SessionId, 1, 1, "given_names", "X", "X", 0.5,
            (FieldEvidenceSource)999, null, ExtractedAt, EvidenceClassification.Personal));
    }

    [Fact]
    public void rejects_an_undefined_classification_value()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DocumentFieldEvidence.Create(
            TenantId, SessionId, 1, 1, "given_names", "X", "X", 0.5,
            FieldEvidenceSource.Ocr, null, ExtractedAt, (EvidenceClassification)999));
    }
}

/// <summary>Developer tests for the bounding-box value object AC03 requires alongside a field.</summary>
public sealed class BoundingBoxTests
{
    [Fact]
    public void accepts_a_box_fully_inside_the_unit_square()
    {
        var box = BoundingBox.Create(0.1, 0.1, 0.5, 0.5);
        Assert.Equal(0.1, box.X);
        Assert.Equal(0.5, box.Width);
    }

    [Theory]
    [InlineData(0.9, 0.0, 0.2, 0.1)] // x + width > 1
    [InlineData(0.0, 0.95, 0.1, 0.2)] // y + height > 1
    [InlineData(-0.1, 0.0, 0.2, 0.2)] // negative origin
    [InlineData(0.0, 0.0, 0.0, 0.2)] // zero width
    public void rejects_a_box_that_does_not_fit_in_the_unit_square(double x, double y, double w, double h)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BoundingBox.Create(x, y, w, h));
    }

    // QA-02 (Seyi review): NaN fails every comparison operator, so it previously slipped past
    // both the per-coordinate range checks and the x+width/y+height sum checks silently.
    [Theory]
    [InlineData(double.NaN, 0.1, 0.2, 0.2)]
    [InlineData(0.1, double.NaN, 0.2, 0.2)]
    [InlineData(0.1, 0.1, double.NaN, 0.2)]
    [InlineData(0.1, 0.1, 0.2, double.NaN)]
    [InlineData(double.PositiveInfinity, 0.1, 0.2, 0.2)]
    [InlineData(0.1, 0.1, double.NegativeInfinity, 0.2)]
    public void rejects_a_non_finite_coordinate(double x, double y, double w, double h)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BoundingBox.Create(x, y, w, h));
    }
}
