using Biolog.Features.BarcodeEngine.Model;

namespace Biolog.Features.DocumentClassifier.Model;

/// <summary>
/// One catalog entry the Document Intelligence Registry holds (BIO-DOC-006-AC03): the identity a
/// document is classified as - <see cref="Country"/> (ICAO Doc 9303 3-letter code, the same
/// alphabet MRZ's <c>IssuingState</c> field already reads - reusing it means an MRZ read can
/// corroborate a candidate without a separate country-name mapping table), <see cref="DocumentClass"/>
/// (a data-driven string, e.g. "passport"/"national-id"/"drivers-licence" - not a hard-coded enum,
/// the same "registered, not switched" shape BIO-DOC-002's <c>MrzFormatRegistry</c> and
/// BIO-DOC-004's <c>OcrTemplateRegistry</c> already use, since the platform will keep adding
/// document classes this story cannot enumerate in advance), and <see cref="Series"/> (a free-form
/// version/series label, e.g. "2013-polymer" vs "2021-redesign" - the one part of AC01's identity
/// tuple no sibling engine's evidence names on its own; see <see cref="Classifier"/>'s
/// remarks for why a registered OCR template is what actually supplies it) - together with which
/// <see cref="Side"/> of the physical document this entry describes and the
/// <see cref="TemplateId"/> (BIO-DOC-004's <c>OcrDocumentTemplate.TemplateId</c>) that should be
/// used to read it.
///
/// <para>
/// <see cref="ExpectedSymbologies"/> is optional corroborating evidence, not a requirement: an
/// entry for a document class that carries no barcode at all legitimately declares an empty list.
/// </para>
/// </summary>
public sealed record DocumentIntelligenceRegistryEntry(
    string EntryId,
    string Country,
    string DocumentClass,
    string Series,
    DocumentSide Side,
    int VersionNumber,
    string TemplateId,
    IReadOnlyList<BarcodeSymbology> ExpectedSymbologies);
