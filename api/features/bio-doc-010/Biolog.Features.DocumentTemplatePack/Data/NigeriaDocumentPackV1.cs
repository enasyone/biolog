using Biolog.Features.DocumentClassifier.Model;
using Biolog.Features.DocumentTemplatePack.Model;

namespace Biolog.Features.DocumentTemplatePack.Data;

/// <summary>
/// The actual governed content of "the Nigeria Document Intelligence Pack v1" (BIO-DOC-010's own
/// user story: "a governed Nigeria-first document-template pack that can expand to West Africa,
/// Africa and global coverage"). Three entries, chosen to exercise every one of AC01's metadata
/// kinds across the set: a chip-bearing MRZ document (passport), a document with neither MRZ nor
/// chip (NIN slip), and a two-sided document (driver's licence). Every field value anywhere in
/// this file is a fabricated placeholder (SYNTHETIC data only, per this repository's standing
/// constraint) - no real document numbers, names or identifiers.
/// </summary>
public static class NigeriaDocumentPackV1
{
    public const string PackId = "nigeria-document-pack";
    public const string PackVersion = "nigeria-document-pack-v1";
    public const string CountryScope = "NGA";

    public const string PassportEntryId = "nga-passport-2021";
    public const string NinSlipEntryId = "nga-nin-slip-2014";
    public const string DriversLicenceEntryId = "nga-drivers-licence-2018";

    /// <summary>Builds the DRAFT pack with all three governed entries added - the caller
    /// (<see cref="Api.DocumentTemplatePackService"/>) is the one that calls
    /// <see cref="Model.DocumentTemplatePack.Publish"/>, since only it holds the
    /// <see cref="DocumentIntelligenceRegistry"/>/audit sink instances that requires.</summary>
    public static Model.DocumentTemplatePack Build()
    {
        var pack = new Model.DocumentTemplatePack(PackId, PackVersion, CountryScope);
        pack.AddEntry(PassportEntry());
        pack.AddEntry(NinSlipEntry());
        pack.AddEntry(DriversLicenceEntry());
        return pack;
    }

    public static DocumentTemplatePackEntry PassportEntry() => DocumentTemplatePackEntry.Create(
        entryId: PassportEntryId,
        country: "NGA",
        issuer: "Nigeria Immigration Service",
        documentClass: "passport",
        series: "2021-ecowas-epassport",
        versionNumber: 1,
        sides: [new DocumentTemplatePackSide(DocumentSide.Front, "tpl-nga-passport-2021-front")],
        expectedFields: ["Surname", "GivenNames", "PassportNumber", "DateOfBirth", "Sex", "DateOfExpiry", "NationalIdentityNumber"],
        mrz: new MrzMetadata(Supported: true, FormatId: "TD3", Note: "ICAO Doc 9303 TD3 two-line MRZ on the biodata page."),
        nfc: new NfcMetadata(Supported: true, ChipStandard: "BAC", Note: "ePassport chip supports Basic Access Control passive authentication."),
        securityFeatures: ["polymer-biodata-page", "laser-perforated-passport-number", "uv-reactive-ink", "ghost-image"],
        validationRules:
        [
            new FieldValidationRule("Surname", Required: true, Pattern: null),
            new FieldValidationRule("PassportNumber", Required: true, Pattern: @"^[A-Z][0-9]{8}$"),
            new FieldValidationRule("DateOfBirth", Required: true, Pattern: @"^\d{4}-\d{2}-\d{2}$"),
            new FieldValidationRule("NationalIdentityNumber", Required: false, Pattern: @"^\d{11}$"),
        ]);

    public static DocumentTemplatePackEntry NinSlipEntry() => DocumentTemplatePackEntry.Create(
        entryId: NinSlipEntryId,
        country: "NGA",
        issuer: "National Identity Management Commission",
        documentClass: "national-id",
        series: "2014-nin-slip",
        versionNumber: 1,
        sides: [new DocumentTemplatePackSide(DocumentSide.Front, "tpl-nga-nin-slip-2014-front")],
        expectedFields: ["Surname", "GivenNames", "NIN", "DateOfBirth", "TrackingId"],
        mrz: MrzMetadata.NotSupported("The NIN slip carries no machine-readable zone."),
        nfc: NfcMetadata.NotSupported("Paper slip - no embedded chip."),
        securityFeatures: ["nimc-watermark", "qr-code-verification-link"],
        validationRules:
        [
            new FieldValidationRule("Surname", Required: true, Pattern: null),
            new FieldValidationRule("NIN", Required: true, Pattern: @"^\d{11}$"),
            new FieldValidationRule("TrackingId", Required: false, Pattern: null),
        ]);

    public static DocumentTemplatePackEntry DriversLicenceEntry() => DocumentTemplatePackEntry.Create(
        entryId: DriversLicenceEntryId,
        country: "NGA",
        issuer: "Federal Road Safety Corps",
        documentClass: "drivers-licence",
        series: "2018-polymer",
        versionNumber: 1,
        sides:
        [
            new DocumentTemplatePackSide(DocumentSide.Front, "tpl-nga-dl-2018-front"),
            new DocumentTemplatePackSide(DocumentSide.Back, "tpl-nga-dl-2018-back"),
        ],
        expectedFields: ["Surname", "GivenNames", "LicenceNumber", "DateOfBirth", "DateOfExpiry", "LicenceClass"],
        mrz: MrzMetadata.NotSupported("The polymer driver's licence carries no machine-readable zone."),
        nfc: NfcMetadata.NotSupported("Polymer card - no embedded chip."),
        securityFeatures: ["polymer-substrate", "hologram-overlay", "microprint-border"],
        validationRules:
        [
            new FieldValidationRule("Surname", Required: true, Pattern: null),
            new FieldValidationRule("LicenceNumber", Required: true, Pattern: @"^[A-Z]{3}-\d{6}-[A-Z]{2}\d{2}$"),
            new FieldValidationRule("LicenceClass", Required: false, Pattern: @"^[A-G]$"),
        ]);

    /// <summary>AC03's governed, synthetic reference samples - one "happy path" sample per entry,
    /// each one that satisfies its own entry's validation rules exactly.</summary>
    public static IReadOnlyList<DocumentTemplateSample> Samples() =>
    [
        new DocumentTemplateSample(
            "sample-nga-passport-2021-happy-path", PassportEntryId, DocumentSide.Front, "happy-path",
            HappyPathPassportFields()),
        new DocumentTemplateSample(
            "sample-nga-nin-slip-2014-happy-path", NinSlipEntryId, DocumentSide.Front, "happy-path",
            HappyPathNinSlipFields()),
        new DocumentTemplateSample(
            "sample-nga-drivers-licence-2018-happy-path", DriversLicenceEntryId, DocumentSide.Front, "happy-path",
            HappyPathDriversLicenceFields()),
    ];

    /// <summary>
    /// The three deterministic demo cycle steps <see cref="Api.DocumentTemplatePackService.IdentifyNextDemo"/>
    /// walks through, one per call (mirroring BIO-DOC-009's own 3-fixture demo cycle shape): a clean
    /// pass (<see cref="DocumentTemplateVerificationState.Consistent"/>), a missing required field
    /// (<see cref="DocumentTemplateVerificationState.Failed"/>), and a non-required field pattern
    /// mismatch (<see cref="DocumentTemplateVerificationState.Inconclusive"/>) - every one of AC04's
    /// three reachable states is exercised over the real HTTP route, not just in a unit test.
    /// </summary>
    public static IReadOnlyList<(string EntryId, IReadOnlyDictionary<string, string> ObservedFields)> DemoScenarios() =>
    [
        (PassportEntryId, HappyPathPassportFields()),
        (NinSlipEntryId, MissingRequiredFieldNinSlipFields()),
        (DriversLicenceEntryId, NonRequiredPatternMismatchDriversLicenceFields()),
    ];

    private static IReadOnlyDictionary<string, string> HappyPathPassportFields() => new Dictionary<string, string>
    {
        ["Surname"] = "ADEYEMI",
        ["GivenNames"] = "CHIOMA FUNMILAYO",
        ["PassportNumber"] = "A12345678",
        ["DateOfBirth"] = "1990-05-14",
        ["Sex"] = "F",
        ["DateOfExpiry"] = "2031-05-13",
        ["NationalIdentityNumber"] = "12345678901",
    };

    private static IReadOnlyDictionary<string, string> HappyPathNinSlipFields() => new Dictionary<string, string>
    {
        ["Surname"] = "OKONKWO",
        ["GivenNames"] = "EMEKA JOHN",
        ["NIN"] = "98765432109",
        ["DateOfBirth"] = "1988-11-02",
        ["TrackingId"] = "TRK-2014-000123",
    };

    private static IReadOnlyDictionary<string, string> MissingRequiredFieldNinSlipFields() => new Dictionary<string, string>
    {
        ["Surname"] = "BELLO",
        ["GivenNames"] = "AISHA",
        // NIN deliberately omitted - required field missing (DocumentTemplateVerificationState.Failed).
        ["DateOfBirth"] = "1995-03-21",
    };

    private static IReadOnlyDictionary<string, string> HappyPathDriversLicenceFields() => new Dictionary<string, string>
    {
        ["Surname"] = "IBRAHIM",
        ["GivenNames"] = "MUSA",
        ["LicenceNumber"] = "ABC-123456-XY12",
        ["DateOfBirth"] = "1985-09-09",
        ["DateOfExpiry"] = "2029-09-08",
        ["LicenceClass"] = "B",
    };

    private static IReadOnlyDictionary<string, string> NonRequiredPatternMismatchDriversLicenceFields() => new Dictionary<string, string>
    {
        ["Surname"] = "NWACHUKWU",
        ["GivenNames"] = "OBIORA",
        ["LicenceNumber"] = "XYZ-654321-AB01",
        ["DateOfBirth"] = "1979-01-30",
        ["DateOfExpiry"] = "2027-01-29",
        // LicenceClass deliberately malformed - a non-required field pattern mismatch
        // (DocumentTemplateVerificationState.Inconclusive), never a Failed disposition.
        ["LicenceClass"] = "Z9",
    };
}
