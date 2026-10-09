namespace Biolog.Features.DuplicateReview.Domain;

/// <summary>
/// BIO-IDN-005-AC03: "Merge is never automatic." There is deliberately no "Merged" member on
/// this enum, and no method in this project or endpoint this feature maps performs a PERSON
/// merge - see docs/features/bio-idn-005/README.md "AC03 - no merge exists in this codebase".
/// <see cref="ConfirmedDuplicate"/> is routing information for a separate, wholly manual
/// operational process outside this system's automation, not a trigger this feature fires.
/// </summary>
public enum DuplicateReviewStatus
{
    PendingReview,
    ConfirmedDuplicate,
    ConfirmedDistinct,
}

public static class DuplicateReviewStatusExtensions
{
    public static string ToWireString(this DuplicateReviewStatus status) => status switch
    {
        DuplicateReviewStatus.PendingReview => "PENDING_REVIEW",
        DuplicateReviewStatus.ConfirmedDuplicate => "CONFIRMED_DUPLICATE",
        DuplicateReviewStatus.ConfirmedDistinct => "CONFIRMED_DISTINCT",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "unreachable: exhaustive switch"),
    };

    public static bool TryParse(string? value, out DuplicateReviewStatus status)
    {
        switch (value)
        {
            case "PENDING_REVIEW": status = DuplicateReviewStatus.PendingReview; return true;
            case "CONFIRMED_DUPLICATE": status = DuplicateReviewStatus.ConfirmedDuplicate; return true;
            case "CONFIRMED_DISTINCT": status = DuplicateReviewStatus.ConfirmedDistinct; return true;
            default: status = default; return false;
        }
    }
}
