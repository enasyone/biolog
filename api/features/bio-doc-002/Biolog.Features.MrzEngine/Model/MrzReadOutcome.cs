using Biolog.Features.DocumentVerify.Model;

namespace Biolog.Features.MrzEngine.Model;

/// <summary>
/// The end-to-end result of <see cref="MrzReader.Read"/> (BIO-DOC-002-AC01 through AC05): what
/// was detected, what was parsed, whether every check digit that was supposed to be there
/// actually matches, and - AC05's central guarantee - whether a human needs to look at this
/// before it is trusted. <see cref="RequiresManualReview"/> is derived purely from what was
/// actually detected/recognized/validated; nothing in this engine ever substitutes a "probably
/// meant" value to avoid setting it.
///
/// <para>
/// Like <see cref="MrzFieldRead"/>, every accessor is <c>internal init</c>: <see cref="RequiresManualReview"/>
/// is precisely the flag a caller relies on to decide whether this result is safe to trust
/// unattended, so a public <c>init</c>/<c>with</c> would let a caller launder a result that
/// should have required review into one that claims it does not.
/// </para>
/// </summary>
public sealed record MrzReadOutcome
{
    public bool Detected { get; internal init; }
    public MrzFormatKind? Format { get; internal init; }
    public BoundingBox? Region { get; internal init; }
    public IReadOnlyList<MrzFieldRead> Fields { get; internal init; } = [];
    public bool CompositeCheckDigitApplicable { get; internal init; }
    public bool? CompositeCheckDigitValid { get; internal init; }
    public bool RequiresManualReview { get; internal init; }
    public IReadOnlyList<string> ReviewReasons { get; internal init; } = [];

    internal MrzReadOutcome(
        bool detected,
        MrzFormatKind? format,
        BoundingBox? region,
        IReadOnlyList<MrzFieldRead> fields,
        bool compositeCheckDigitApplicable,
        bool? compositeCheckDigitValid,
        bool requiresManualReview,
        IReadOnlyList<string> reviewReasons)
    {
        Detected = detected;
        Format = format;
        Region = region;
        Fields = fields;
        CompositeCheckDigitApplicable = compositeCheckDigitApplicable;
        CompositeCheckDigitValid = compositeCheckDigitValid;
        RequiresManualReview = requiresManualReview;
        ReviewReasons = reviewReasons;
    }

    internal static MrzReadOutcome NotDetected(string reason) => new(
        detected: false,
        format: null,
        region: null,
        fields: [],
        compositeCheckDigitApplicable: false,
        compositeCheckDigitValid: null,
        requiresManualReview: true,
        reviewReasons: [reason]);
}
