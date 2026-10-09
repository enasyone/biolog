using Biolog.Features.BarcodeEngine.Model;

namespace Biolog.Features.ParserRegistry.Model;

/// <summary>Which documents a parser version applies to (BIO-DOC-005-AC03): the barcode
/// symbology it was decoded from (BIO-DOC-003) and the issuing authority's own identifier for the
/// credential format (e.g. an AAMVA-format US state driver's-license code) - not every parser
/// needs a narrower per-document-code match, so that is optional.</summary>
public sealed record ParserApplicability(BarcodeSymbology Symbology, string IssuingAuthority, string? DocumentCode)
{
    public static ParserApplicability Create(BarcodeSymbology symbology, string issuingAuthority, string? documentCode = null)
    {
        if (string.IsNullOrWhiteSpace(issuingAuthority))
        {
            throw new ArgumentException("must be a non-empty string", nameof(issuingAuthority));
        }

        return new ParserApplicability(symbology, issuingAuthority, documentCode);
    }

    public bool Matches(BarcodeSymbology symbology, string issuingAuthority, string? documentCode) =>
        Symbology == symbology
        && IssuingAuthority == issuingAuthority
        && (DocumentCode is null || DocumentCode == documentCode);
}
