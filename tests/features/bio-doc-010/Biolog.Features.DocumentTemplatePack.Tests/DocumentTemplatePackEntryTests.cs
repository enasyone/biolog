using Biolog.Features.DocumentClassifier.Model;
using Biolog.Features.DocumentTemplatePack.Model;
using Biolog.Features.DocumentTemplatePack.Tests.Fixtures;
using Xunit;

namespace Biolog.Features.DocumentTemplatePack.Tests;

/// <summary>BIO-DOC-010-AC01: an entry's structural shape (country/issuer/document type/series-
/// version/sides/expected fields/MRZ/NFC/security metadata/validation rules) is enforced by
/// <see cref="DocumentTemplatePackEntry.Create"/>, not merely documented.</summary>
public sealed class DocumentTemplatePackEntryTests
{
    [Fact]
    public void Create_builds_a_valid_entry_with_every_AC01_metadata_kind_populated()
    {
        var entry = TestEntries.SimpleEntry();

        Assert.Equal("NGA", entry.Country);
        Assert.Equal("Test Issuer", entry.Issuer);
        Assert.Equal("test-document", entry.DocumentClass);
        Assert.Equal("test-series", entry.Series);
        Assert.Equal(1, entry.VersionNumber);
        Assert.Single(entry.Sides);
        Assert.Equal(DocumentSide.Front, entry.Sides[0].Side);
        Assert.Equal(3, entry.ExpectedFields.Count);
        Assert.False(entry.Mrz.Supported);
        Assert.False(entry.Nfc.Supported);
        Assert.Single(entry.SecurityFeatures);
        Assert.Equal(3, entry.ValidationRules.Count);
    }

    [Fact]
    public void Create_rejects_zero_sides()
    {
        var ex = Assert.Throws<ArgumentException>(() => DocumentTemplatePackEntry.Create(
            "e1", "NGA", "Issuer", "class", "series", 1,
            sides: [],
            expectedFields: ["Field"],
            mrz: MrzMetadata.NotSupported("n/a"),
            nfc: NfcMetadata.NotSupported("n/a"),
            securityFeatures: [],
            validationRules: []));
        Assert.Contains("at least one physical side", ex.Message);
    }

    [Fact]
    public void Create_rejects_a_duplicate_side()
    {
        var ex = Assert.Throws<ArgumentException>(() => DocumentTemplatePackEntry.Create(
            "e1", "NGA", "Issuer", "class", "series", 1,
            sides: [new DocumentTemplatePackSide(DocumentSide.Front, "tpl-a"), new DocumentTemplatePackSide(DocumentSide.Front, "tpl-b")],
            expectedFields: ["Field"],
            mrz: MrzMetadata.NotSupported("n/a"),
            nfc: NfcMetadata.NotSupported("n/a"),
            securityFeatures: [],
            validationRules: []));
        Assert.Contains("cannot declare the same side twice", ex.Message);
    }

    [Fact]
    public void Create_rejects_zero_expected_fields()
    {
        var ex = Assert.Throws<ArgumentException>(() => DocumentTemplatePackEntry.Create(
            "e1", "NGA", "Issuer", "class", "series", 1,
            sides: [new DocumentTemplatePackSide(DocumentSide.Front, "tpl-a")],
            expectedFields: [],
            mrz: MrzMetadata.NotSupported("n/a"),
            nfc: NfcMetadata.NotSupported("n/a"),
            securityFeatures: [],
            validationRules: []));
        Assert.Contains("at least one expected field", ex.Message);
    }

    [Fact]
    public void Create_rejects_a_duplicate_expected_field()
    {
        var ex = Assert.Throws<ArgumentException>(() => DocumentTemplatePackEntry.Create(
            "e1", "NGA", "Issuer", "class", "series", 1,
            sides: [new DocumentTemplatePackSide(DocumentSide.Front, "tpl-a")],
            expectedFields: ["Field", "Field"],
            mrz: MrzMetadata.NotSupported("n/a"),
            nfc: NfcMetadata.NotSupported("n/a"),
            securityFeatures: [],
            validationRules: []));
        Assert.Contains("cannot repeat", ex.Message);
    }

    [Fact]
    public void Create_rejects_a_validation_rule_naming_a_field_not_in_expected_fields()
    {
        // AC01's validation rules govern the fields AC01 itself declares - a rule for an
        // undeclared field would validate something no reader was ever told to expect.
        var ex = Assert.Throws<ArgumentException>(() => DocumentTemplatePackEntry.Create(
            "e1", "NGA", "Issuer", "class", "series", 1,
            sides: [new DocumentTemplatePackSide(DocumentSide.Front, "tpl-a")],
            expectedFields: ["Field"],
            mrz: MrzMetadata.NotSupported("n/a"),
            nfc: NfcMetadata.NotSupported("n/a"),
            securityFeatures: [],
            validationRules: [new FieldValidationRule("SomeOtherField", Required: true, Pattern: null)]));
        Assert.Contains("not one of this entry's own expected fields", ex.Message);
    }

    [Fact]
    public void Create_rejects_version_number_below_one()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DocumentTemplatePackEntry.Create(
            "e1", "NGA", "Issuer", "class", "series", 0,
            sides: [new DocumentTemplatePackSide(DocumentSide.Front, "tpl-a")],
            expectedFields: ["Field"],
            mrz: MrzMetadata.NotSupported("n/a"),
            nfc: NfcMetadata.NotSupported("n/a"),
            securityFeatures: [],
            validationRules: []));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_a_blank_entryId(string blank)
    {
        Assert.Throws<ArgumentException>(() => DocumentTemplatePackEntry.Create(
            blank, "NGA", "Issuer", "class", "series", 1,
            sides: [new DocumentTemplatePackSide(DocumentSide.Front, "tpl-a")],
            expectedFields: ["Field"],
            mrz: MrzMetadata.NotSupported("n/a"),
            nfc: NfcMetadata.NotSupported("n/a"),
            securityFeatures: [],
            validationRules: []));
    }
}
