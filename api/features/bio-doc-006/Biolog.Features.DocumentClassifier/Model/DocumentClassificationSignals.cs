using Biolog.Features.BarcodeEngine.Model;
using Biolog.Features.MrzEngine.Model;
using Biolog.Features.OcrEngine.Model;

namespace Biolog.Features.DocumentClassifier.Model;

/// <summary>
/// The evidence <see cref="Classifier.Classify"/> composes (BIO-DOC-006-AC01). This story
/// does not itself decode MRZ/barcodes/OCR text - it is handed the outcomes BIO-DOC-002/003/004
/// already produced for the same capture, the identical "compose already-produced evidence" shape
/// BIO-DOC-001's <c>MrzFieldRead.ToDocumentFieldEvidence</c> adapter used.
///
/// <para>
/// <see cref="TemplateMatches"/> is keyed by <c>OcrDocumentTemplate.TemplateId</c>: the caller has
/// already run <c>TemplateOcrEngine.Read</c> against every template it wants tried as a candidate
/// (ordinarily every template a <see cref="DocumentIntelligenceRegistry"/> has registered). This is
/// deliberate, not an oversight - see <see cref="Classifier"/>'s remarks for why only a
/// template match can name AC01's "series/version", and why MRZ/barcode evidence corroborates a
/// template match rather than producing a candidate on its own.
/// </para>
/// </summary>
public sealed record DocumentClassificationSignals(
    MrzReadOutcome? Mrz,
    IReadOnlyList<BarcodeDecodeResult> Barcodes,
    IReadOnlyDictionary<string, TemplateOcrResult> TemplateMatches,
    DocumentSide? ExpectedSide);
