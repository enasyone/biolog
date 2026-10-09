namespace Biolog.Features.DocumentVerify.Model;

/// <summary>
/// Which physical side/page of a source document a <see cref="DocumentEvidence"/> object was
/// captured from (BIO-DOC-001-AC02: "page/side metadata").
/// </summary>
public enum DocumentSide
{
    Front,
    Back,
    DataPage,
    SignaturePage,

    /// <summary>An NFC read has no physical page/side; it is still recorded, explicitly, rather
    /// than left to a caller's guess (mirrors ADR-0003's "required, explicitly null otherwise"
    /// posture for the audit envelope's <c>device</c> field).</summary>
    Nfc,

    Other,
}
