namespace Biolog.Features.DocumentClassifier.Model;

/// <summary>
/// The end-to-end result of <see cref="Classifier.Classify"/> (BIO-DOC-006-AC01 through
/// AC05). <see cref="RequiresOperatorConfirmation"/> is derived purely from what was actually
/// matched/scored - the same "never substitute a value to make a result look clean" discipline
/// every prior BIO-DOC-00x <c>RequiresManualReview</c> flag this epic already follows. Locked down
/// the same way for the same reason: every accessor is <c>internal init</c> with an explicit
/// <c>internal</c> constructor, since <see cref="Unknown"/>/<see cref="SideMismatch"/>/
/// <see cref="RequiresOperatorConfirmation"/> are exactly the flags a caller relies on to decide
/// whether this outcome is safe to act on unattended.
/// </summary>
public sealed record DocumentClassificationOutcome
{
    /// <summary>Ranked best-first (AC01). Empty when nothing scored - see <see cref="Unknown"/>.</summary>
    public IReadOnlyList<DocumentClassCandidate> Candidates { get; internal init; } = [];

    /// <summary>AC02: true when no candidate cleared <see cref="Classifier.MinAcceptableConfidence"/>
    /// (including when <see cref="Candidates"/> is empty). A valid, safe outcome - never a forced
    /// pick of the best-scoring-but-still-weak candidate.</summary>
    public bool Unknown { get; internal init; }

    /// <summary>The side of the top candidate's registered template, or <see cref="DocumentSide.Unknown"/>
    /// when <see cref="Unknown"/> is true.</summary>
    public DocumentSide DetectedSide { get; internal init; }

    public DocumentSide? ExpectedSide { get; internal init; }

    /// <summary>AC04: true when the caller asserted an expected side, classification is not
    /// <see cref="Unknown"/>, and <see cref="DetectedSide"/> disagrees with it.</summary>
    public bool SideMismatch { get; internal init; }

    /// <summary>AC05: true whenever this outcome should not be trusted unattended - covers
    /// <see cref="Unknown"/>, <see cref="SideMismatch"/>, an ambiguous top-two candidate score, and
    /// a winning template match that itself required manual review.</summary>
    public bool RequiresOperatorConfirmation { get; internal init; }

    public IReadOnlyList<string> ConfirmationReasons { get; internal init; } = [];

    /// <summary>AC03: the template to use, resolved via the winning candidate's
    /// <see cref="DocumentIntelligenceRegistryEntry"/>. Null when <see cref="Unknown"/>.</summary>
    public string? SelectedTemplateId { get; internal init; }

    public DocumentClassCandidate? Best => Candidates.Count > 0 ? Candidates[0] : null;

    internal DocumentClassificationOutcome(
        IReadOnlyList<DocumentClassCandidate> candidates,
        bool unknown,
        DocumentSide detectedSide,
        DocumentSide? expectedSide,
        bool sideMismatch,
        bool requiresOperatorConfirmation,
        IReadOnlyList<string> confirmationReasons,
        string? selectedTemplateId)
    {
        Candidates = candidates;
        Unknown = unknown;
        DetectedSide = detectedSide;
        ExpectedSide = expectedSide;
        SideMismatch = sideMismatch;
        RequiresOperatorConfirmation = requiresOperatorConfirmation;
        ConfirmationReasons = confirmationReasons;
        SelectedTemplateId = selectedTemplateId;
    }
}
