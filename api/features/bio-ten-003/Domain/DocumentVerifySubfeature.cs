namespace Biolog.Features.TenantFeatureConfig.Domain;

/// <summary>
/// BIO-TEN-003-AC01: the closed set of eight Document Verify subfeature toggles. Taken
/// verbatim from <c>web/lib/feature-mode.ts</c>'s own documented comment on
/// <c>DocumentVerifyService</c>'s ports - "the other seven Document Verify steps (capture,
/// MRZ, barcode, OCR, document identification, NFC, selfie comparison)" - plus
/// <c>authenticity</c>, named separately in that same file as its own real BIO-DOC-009-backed
/// port key. Eight total, exactly matching the frontend's own established naming, not
/// invented here.
/// </summary>
public enum DocumentVerifySubfeature
{
    Capture,
    Mrz,
    Barcode,
    Ocr,
    Authenticity,
    DocumentIdentification,
    Nfc,
    SelfieComparison,
}

public static class DocumentVerifySubfeatureExtensions
{
    public static string ToWireString(this DocumentVerifySubfeature subfeature) => subfeature switch
    {
        DocumentVerifySubfeature.Capture => "capture",
        DocumentVerifySubfeature.Mrz => "mrz",
        DocumentVerifySubfeature.Barcode => "barcode",
        DocumentVerifySubfeature.Ocr => "ocr",
        DocumentVerifySubfeature.Authenticity => "authenticity",
        DocumentVerifySubfeature.DocumentIdentification => "document_identification",
        DocumentVerifySubfeature.Nfc => "nfc",
        DocumentVerifySubfeature.SelfieComparison => "selfie_comparison",
        _ => throw new ArgumentOutOfRangeException(nameof(subfeature), subfeature, "unreachable: exhaustive switch"),
    };

    public static bool TryParse(string? value, out DocumentVerifySubfeature subfeature)
    {
        switch (value)
        {
            case "capture": subfeature = DocumentVerifySubfeature.Capture; return true;
            case "mrz": subfeature = DocumentVerifySubfeature.Mrz; return true;
            case "barcode": subfeature = DocumentVerifySubfeature.Barcode; return true;
            case "ocr": subfeature = DocumentVerifySubfeature.Ocr; return true;
            case "authenticity": subfeature = DocumentVerifySubfeature.Authenticity; return true;
            case "document_identification": subfeature = DocumentVerifySubfeature.DocumentIdentification; return true;
            case "nfc": subfeature = DocumentVerifySubfeature.Nfc; return true;
            case "selfie_comparison": subfeature = DocumentVerifySubfeature.SelfieComparison; return true;
            default: subfeature = default; return false;
        }
    }
}
