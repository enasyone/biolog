namespace Biolog.Features.DocumentVerify.Model;

/// <summary>How a <see cref="DocumentEvidence"/> object entered the system (BIO-DOC-001-AC03).</summary>
public enum EvidenceSource
{
    /// <summary>A file uploaded through a web/mobile client.</summary>
    Upload,

    /// <summary>Captured live by a camera/scanner at a device (BIO-DEV-001).</summary>
    Capture,

    /// <summary>Read from a document's embedded NFC chip (BIO-DOC-007).</summary>
    NfcRead,
}

/// <summary>Which downstream engine produced a <see cref="DocumentFieldEvidence"/> record
/// (BIO-DOC-001-AC03). Registered here as the engines this foundation exists for (spec:
/// MRZ, Barcode, OCR, NFC) plus <see cref="Manual"/> for a human-entered correction.</summary>
public enum FieldEvidenceSource
{
    Mrz,
    Barcode,
    Ocr,
    Nfc,
    Manual,
}
