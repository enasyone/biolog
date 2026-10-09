using System.Text;
using Biolog.Features.BarcodeEngine.Model;
using Biolog.Features.EvidenceComparison.Model;
using Biolog.Features.EvidenceComparison.Tests.Fixtures;
using Biolog.Features.MrzEngine.Model;
using Biolog.Features.ParserRegistry.Model;
using Xunit;

namespace Biolog.Features.EvidenceComparison.Tests;

/// <summary>Developer tests for BIO-DOC-008-AC01 (source-by-source values) and AC02
/// (normalization never destroys raw evidence), exercised against real BIO-DOC-002/BIO-DOC-005
/// engine output, never fabricated - those result types are locked down for exactly this reason.</summary>
public sealed class SourceFieldValueTests
{
    private const string Now = "2026-09-23T12:00:00.000Z";
    private static readonly ParserCaller.Bts Bts = new();

    [Fact]
    public void AC01_Create_carries_source_raw_normalized_and_confidence_together()
    {
        var value = SourceFieldValue.Create(EvidenceSourceKind.Visual, "OKAFOR", "OKAFOR", 0.95);

        Assert.Equal(EvidenceSourceKind.Visual, value.Source);
        Assert.Equal("OKAFOR", value.RawValue);
        Assert.Equal("OKAFOR", value.NormalizedValue);
        Assert.Equal(0.95, value.Confidence);
    }

    [Fact]
    public void AC02_Create_rejects_an_empty_normalized_value_never_silently_drops_it()
    {
        Assert.Throws<ArgumentException>(() => SourceFieldValue.Create(EvidenceSourceKind.Visual, "OKAFOR", "", 0.9));
    }

    [Fact]
    public void AC02_Create_rejects_an_empty_raw_value()
    {
        Assert.Throws<ArgumentException>(() => SourceFieldValue.Create(EvidenceSourceKind.Visual, "", "OKAFOR", 0.9));
    }

    [Fact]
    public void Create_rejects_a_confidence_outside_zero_to_one()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SourceFieldValue.Create(EvidenceSourceKind.Visual, "X", "X", 1.5));
        Assert.Throws<ArgumentOutOfRangeException>(() => SourceFieldValue.Create(EvidenceSourceKind.Visual, "X", "X", -0.1));
    }

    [Fact]
    public void AC01_FromMrz_adapts_a_real_document_number_field_read_by_the_mrz_engine()
    {
        var canvas = new SyntheticMrzCanvas(width: 270, height: 40);
        canvas.DrawMrzLines([MrzSampleData.Td3Line1, MrzSampleData.Td3Line2], left: 3, top: 15);

        var outcome = MrzReader.Read(canvas.ToRaster());
        var documentNumber = outcome.Fields.Single(f => f.FieldName == "document_number");

        var value = SourceFieldValue.FromMrz(documentNumber);

        Assert.Equal(EvidenceSourceKind.Mrz, value.Source);
        Assert.Equal(documentNumber.RawValue, value.RawValue);
        Assert.Equal(documentNumber.NormalizedValue, value.NormalizedValue);
        Assert.Equal(documentNumber.Confidence, value.Confidence);
    }

    [Fact]
    public void AC01_FromParsedBarcodeField_adapts_a_real_published_parsers_field_result()
    {
        var catalog = new ParserCatalog(new TenantParserAuthorityRegistry());
        var applicability = ParserApplicability.Create(BarcodeSymbology.Pdf417, "US-CA-DMV");
        var fields = new[] { ParserFieldMapping.Create("surname", 0, 6) };
        var record = catalog.Draft(Bts, "p1", new ParserOwnerScope.Global(), applicability, fields, [], "author", Now);
        catalog.RecordTestResult(Bts, "p1", true, "tester", record.ConcurrencyVersion, Now);
        catalog.Publish(Bts, "p1", "approver", record.ConcurrencyVersion, Now);

        var result = ParserExecutor.Apply(record.Latest, Encoding.UTF8.GetBytes("OKAFOR"));
        var field = result.Fields[0];

        var value = SourceFieldValue.FromParsedBarcodeField(field);

        Assert.Equal(EvidenceSourceKind.Barcode, value.Source);
        Assert.Equal("OKAFOR", value.RawValue);
        Assert.Equal("OKAFOR", value.NormalizedValue);
        Assert.Equal(1.0, value.Confidence); // field.Valid was true
    }

    [Fact]
    public void AC02_FromParsedBarcodeField_a_failed_validation_maps_to_zero_confidence_not_a_fabricated_one()
    {
        var catalog = new ParserCatalog(new TenantParserAuthorityRegistry());
        var applicability = ParserApplicability.Create(BarcodeSymbology.Pdf417, "US-CA-DMV");
        var fields = new[] { ParserFieldMapping.Create("surname", 0, 6) };
        var rules = new[] { ParserValidationRule.Create("surname", required: true, pattern: "^[A-Z]+$") };
        var record = catalog.Draft(Bts, "p1", new ParserOwnerScope.Global(), applicability, fields, rules, "author", Now);
        catalog.RecordTestResult(Bts, "p1", true, "tester", record.ConcurrencyVersion, Now);
        catalog.Publish(Bts, "p1", "approver", record.ConcurrencyVersion, Now);

        var result = ParserExecutor.Apply(record.Latest, Encoding.UTF8.GetBytes("123456")); // fails the pattern
        var field = result.Fields[0];
        Assert.False(field.Valid);

        var value = SourceFieldValue.FromParsedBarcodeField(field);

        Assert.Equal(0.0, value.Confidence);
        Assert.Equal("123456", value.RawValue); // the raw value is still preserved, never discarded (AC02)
    }
}
