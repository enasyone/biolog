namespace Biolog.Features.EvidenceComparison.Model;

/// <summary>The four evidence sources BIO-DOC-008's own user story names ("field-level comparison
/// across visual OCR, MRZ, barcode and NFC evidence"). <see cref="Nfc"/> is deliberately not a
/// source of decoded field VALUES here - see <see cref="NfcCorroboration"/>'s remarks for why.</summary>
public enum EvidenceSourceKind
{
    Visual,
    Mrz,
    Barcode,
    Nfc,
}
