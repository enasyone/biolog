using Biolog.Features.DocumentClassifier.Model;
using Biolog.Features.DocumentTemplatePack.Model;

namespace Biolog.Features.DocumentTemplatePack.Tests.Fixtures;

/// <summary>Small, hand-built entries for tests that exercise <see cref="DocumentTemplatePack"/>/
/// <see cref="DocumentTemplateVerification"/> logic in isolation, independent of the real
/// <c>Data.NigeriaDocumentPackV1</c> seed content (that content has its own dedicated
/// <c>NigeriaDocumentPackV1Tests</c>).</summary>
public static class TestEntries
{
    public static DocumentTemplatePackEntry SimpleEntry(string entryId = "test-entry-1", int versionNumber = 1) => DocumentTemplatePackEntry.Create(
        entryId: entryId,
        country: "NGA",
        issuer: "Test Issuer",
        documentClass: "test-document",
        series: "test-series",
        versionNumber: versionNumber,
        sides: [new DocumentTemplatePackSide(DocumentSide.Front, $"tpl-{entryId}-front")],
        expectedFields: ["Surname", "DocumentNumber", "OptionalNote"],
        mrz: MrzMetadata.NotSupported("test fixture - MRZ not modelled"),
        nfc: NfcMetadata.NotSupported("test fixture - NFC not modelled"),
        securityFeatures: ["test-security-feature"],
        validationRules:
        [
            new FieldValidationRule("Surname", Required: true, Pattern: null),
            new FieldValidationRule("DocumentNumber", Required: true, Pattern: @"^[A-Z]\d{4}$"),
            new FieldValidationRule("OptionalNote", Required: false, Pattern: @"^[a-z]+$"),
        ]);

    public static IReadOnlyDictionary<string, string> ValidObservedFields() => new Dictionary<string, string>
    {
        ["Surname"] = "TESTFAMILY",
        ["DocumentNumber"] = "A1234",
        ["OptionalNote"] = "note",
    };

    public static IReadOnlyDictionary<string, string> MissingRequiredFieldObservedFields() => new Dictionary<string, string>
    {
        ["DocumentNumber"] = "A1234",
    };

    public static IReadOnlyDictionary<string, string> NonRequiredPatternMismatchObservedFields() => new Dictionary<string, string>
    {
        ["Surname"] = "TESTFAMILY",
        ["DocumentNumber"] = "A1234",
        ["OptionalNote"] = "NOT-LOWERCASE",
    };
}
