namespace Biolog.Features.OcrEngine.Model;

/// <summary>
/// The end-to-end result of <see cref="TemplateOcrEngine.Read"/> (BIO-DOC-004-AC01/AC04/AC05).
/// <see cref="RequiresManualReview"/> is derived purely from actual per-field confidence against
/// <see cref="TemplateOcrEngine.MinAcceptableConfidence"/> for fields marked
/// <see cref="OcrTemplateField.Critical"/> - never a value this engine substitutes to make a
/// result look clean (AC05: "OCR never uses generative guessing to fill unreadable identity
/// characters"). Locked down the same way as BIO-DOC-002's <c>MrzReadOutcome</c> and
/// BIO-DOC-003's <c>BarcodeDecodeResult</c> - see <see cref="OcrFieldRead"/>'s remarks.
/// </summary>
public sealed record TemplateOcrResult
{
    public IReadOnlyList<OcrFieldRead> Fields { get; internal init; } = [];
    public bool RequiresManualReview { get; internal init; }
    public IReadOnlyList<string> ReviewReasons { get; internal init; } = [];

    internal TemplateOcrResult(IReadOnlyList<OcrFieldRead> fields, bool requiresManualReview, IReadOnlyList<string> reviewReasons)
    {
        Fields = fields;
        RequiresManualReview = requiresManualReview;
        ReviewReasons = reviewReasons;
    }
}
