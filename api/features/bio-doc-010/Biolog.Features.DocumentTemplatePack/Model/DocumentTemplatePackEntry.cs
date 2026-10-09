namespace Biolog.Features.DocumentTemplatePack.Model;

/// <summary>
/// One catalog entry the Nigeria Document Intelligence Pack governs (BIO-DOC-010-AC01): the
/// identity a document is - <see cref="Country"/>, <see cref="Issuer"/>, <see cref="DocumentClass"/>,
/// <see cref="Series"/>/<see cref="VersionNumber"/> - which physical <see cref="Sides"/> it has and
/// the BIO-DOC-006 template each one reads through, which fields a reader should expect
/// (<see cref="ExpectedFields"/>), its MRZ/NFC/security-feature metadata, and the
/// <see cref="ValidationRules"/> a caller can run observed field values against
/// (<see cref="DocumentTemplateVerification"/>).
///
/// <para>
/// Immutable once constructed (AC02, ADR-0001 S3.3 "published ... document template versions ...
/// carry no version field and reject updates outright"): every property is a plain positional
/// record property, and there is no way to mutate one in place - correcting an entry means
/// constructing a new one with a higher <see cref="VersionNumber"/> and adding it to a new pack
/// version (see <see cref="DocumentTemplatePack"/>'s own remarks).
/// </para>
/// </summary>
public sealed record DocumentTemplatePackEntry
{
    public string EntryId { get; }
    public string Country { get; }
    public string Issuer { get; }
    public string DocumentClass { get; }
    public string Series { get; }
    public int VersionNumber { get; }
    public IReadOnlyList<DocumentTemplatePackSide> Sides { get; }
    public IReadOnlyList<string> ExpectedFields { get; }
    public MrzMetadata Mrz { get; }
    public NfcMetadata Nfc { get; }
    public IReadOnlyList<string> SecurityFeatures { get; }
    public IReadOnlyList<FieldValidationRule> ValidationRules { get; }

    private DocumentTemplatePackEntry(
        string entryId,
        string country,
        string issuer,
        string documentClass,
        string series,
        int versionNumber,
        IReadOnlyList<DocumentTemplatePackSide> sides,
        IReadOnlyList<string> expectedFields,
        MrzMetadata mrz,
        NfcMetadata nfc,
        IReadOnlyList<string> securityFeatures,
        IReadOnlyList<FieldValidationRule> validationRules)
    {
        EntryId = entryId;
        Country = country;
        Issuer = issuer;
        DocumentClass = documentClass;
        Series = series;
        VersionNumber = versionNumber;
        Sides = sides;
        ExpectedFields = expectedFields;
        Mrz = mrz;
        Nfc = nfc;
        SecurityFeatures = securityFeatures;
        ValidationRules = validationRules;
    }

    /// <summary>The only sanctioned way to build an entry - validates AC01's structural shape so an
    /// invalid entry can never reach <see cref="DocumentTemplatePack.AddEntry"/> in the first
    /// place.</summary>
    public static DocumentTemplatePackEntry Create(
        string entryId,
        string country,
        string issuer,
        string documentClass,
        string series,
        int versionNumber,
        IReadOnlyList<DocumentTemplatePackSide> sides,
        IReadOnlyList<string> expectedFields,
        MrzMetadata mrz,
        NfcMetadata nfc,
        IReadOnlyList<string> securityFeatures,
        IReadOnlyList<FieldValidationRule> validationRules)
    {
        RequireNonBlank(entryId, nameof(entryId));
        RequireNonBlank(country, nameof(country));
        RequireNonBlank(issuer, nameof(issuer));
        RequireNonBlank(documentClass, nameof(documentClass));
        RequireNonBlank(series, nameof(series));
        ArgumentNullException.ThrowIfNull(mrz);
        ArgumentNullException.ThrowIfNull(nfc);

        if (versionNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(versionNumber), versionNumber, "must be >= 1");
        }

        var sideList = new List<DocumentTemplatePackSide>(sides ?? throw new ArgumentNullException(nameof(sides)));
        if (sideList.Count == 0)
        {
            throw new ArgumentException("an entry must declare at least one physical side", nameof(sides));
        }

        if (sideList.Select(s => s.Side).Distinct().Count() != sideList.Count)
        {
            throw new ArgumentException("an entry cannot declare the same side twice", nameof(sides));
        }

        foreach (var side in sideList)
        {
            RequireNonBlank(side.TemplateId, $"{nameof(sides)}[].{nameof(DocumentTemplatePackSide.TemplateId)}");
        }

        var fieldList = new List<string>(expectedFields ?? throw new ArgumentNullException(nameof(expectedFields)));
        if (fieldList.Count == 0)
        {
            throw new ArgumentException("an entry must declare at least one expected field", nameof(expectedFields));
        }

        if (fieldList.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("an expected field name cannot be blank", nameof(expectedFields));
        }

        if (fieldList.Distinct(StringComparer.Ordinal).Count() != fieldList.Count)
        {
            throw new ArgumentException("expected fields cannot repeat", nameof(expectedFields));
        }

        var ruleList = new List<FieldValidationRule>(validationRules ?? []);
        if (ruleList.Select(r => r.FieldName).Distinct(StringComparer.Ordinal).Count() != ruleList.Count)
        {
            throw new ArgumentException("validation rules cannot repeat the same field name", nameof(validationRules));
        }

        foreach (var rule in ruleList)
        {
            // AC01's validation rules govern the fields AC01 itself declares - a rule for a field
            // this entry never named would validate something no reader was ever told to expect.
            if (!fieldList.Contains(rule.FieldName, StringComparer.Ordinal))
            {
                throw new ArgumentException(
                    $"validation rule names field '{rule.FieldName}', which is not one of this entry's own expected fields",
                    nameof(validationRules));
            }
        }

        return new DocumentTemplatePackEntry(
            entryId, country, issuer, documentClass, series, versionNumber,
            sideList.AsReadOnly(), fieldList.AsReadOnly(), mrz, nfc,
            new List<string>(securityFeatures ?? []).AsReadOnly(), ruleList.AsReadOnly());
    }

    private static void RequireNonBlank(string? value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{paramName} must not be blank", paramName);
        }
    }
}
