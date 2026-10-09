namespace Biolog.Features.DocumentVerify.Model;

/// <summary>State of a human review over a <see cref="DocumentConflict"/> (BIO-DOC-001-AC05).</summary>
public enum ManualReviewState
{
    /// <summary>Raised; no reviewer has picked it up yet.</summary>
    PendingReview,

    /// <summary>A reviewer has claimed it and is actively working it.</summary>
    InReview,

    /// <summary>A reviewer recorded a resolution.</summary>
    Resolved,
}
