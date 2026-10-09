namespace Biolog.Features.ImportConflictReview.Domain;

/// <summary>
/// BIO-INT-005-AC02/AC04. <see cref="KeepExisting"/>/<see cref="UseSource"/>/<see cref="Escalated"/>
/// are the three human decisions AC02 names ("Operator can keep/use source/escalate according
/// to permission"). There is deliberately no "Applied"/"Merged" member on this enum, and no
/// method in this project or endpoint it maps ever writes to BIO-IDN-001's PERSON record -
/// <see cref="UseSource"/> records that a human decided the incoming source value should win,
/// but never applies it, for the same structural reason <see cref="Domain.ImportFieldConflict"/>'s
/// own upstream origin never does: BIO-INT-002's own <c>ImportBatchService</c> header already
/// disclosed that PERSON (BIO-IDN-001) has no field-mutator method at all today - only
/// <c>RegisterAsync</c> (create) and <c>UpdateStatusAsync</c> (lifecycle). <see cref="UseSource"/>
/// is therefore routing information for a future, separately-authorised PERSON-correction
/// capability (the same disclosed scope boundary BIO-INT-002's header already drew, now hit a
/// second time by a second story), not a trigger this feature fires - see
/// docs/features/bio-int-005/README.md "AC04 - no PERSON write exists in this codebase" for the
/// full record. This mirrors BIO-IDN-005-AC03's own "no merge exists in this codebase" shape
/// exactly.
///
/// <see cref="Withdrawn"/> is NOT a human decision - it is set only by
/// <see cref="ImportConflictReviewService.ScanAsync"/> itself, when a PENDING_REVIEW record's
/// external key no longer appears as a conflict in the most recent import batch for that
/// mapping (the underlying source data changed and a later reimport attempt no longer
/// disagrees with the current PERSON value). This keeps the review queue accurate rather than
/// stale, without ever writing to PERSON and without overriding a human's own terminal decision
/// (<see cref="KeepExisting"/>/<see cref="UseSource"/>/<see cref="Escalated"/> are all terminal
/// - once decided, a rescan never reopens or relitigates them, see
/// <see cref="ImportConflictReviewService"/>'s own "never re-decides a decided row" note).
/// </summary>
public enum ImportConflictReviewStatus
{
    PendingReview,
    KeepExisting,
    UseSource,
    Escalated,
    Withdrawn,
}

public static class ImportConflictReviewStatusExtensions
{
    public static string ToWireString(this ImportConflictReviewStatus status) => status switch
    {
        ImportConflictReviewStatus.PendingReview => "PENDING_REVIEW",
        ImportConflictReviewStatus.KeepExisting => "KEEP_EXISTING",
        ImportConflictReviewStatus.UseSource => "USE_SOURCE",
        ImportConflictReviewStatus.Escalated => "ESCALATED",
        ImportConflictReviewStatus.Withdrawn => "WITHDRAWN",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "unreachable: exhaustive switch"),
    };

    /// <summary>Only the three human-decidable outcomes parse here - <see cref="ImportConflictReviewStatus.PendingReview"/>/<see cref="ImportConflictReviewStatus.Withdrawn"/> are never a valid decision request body value (see this enum's own header).</summary>
    public static bool TryParseDecision(string? value, out ImportConflictReviewStatus status)
    {
        switch (value)
        {
            case "KEEP_EXISTING": status = ImportConflictReviewStatus.KeepExisting; return true;
            case "USE_SOURCE": status = ImportConflictReviewStatus.UseSource; return true;
            case "ESCALATED": status = ImportConflictReviewStatus.Escalated; return true;
            default: status = default; return false;
        }
    }

    /// <summary>A decided row's status is terminal - a routine rescan never reopens it (see this enum's own header).</summary>
    public static bool IsTerminalDecision(this ImportConflictReviewStatus status) =>
        status is ImportConflictReviewStatus.KeepExisting or ImportConflictReviewStatus.UseSource or ImportConflictReviewStatus.Escalated;
}
