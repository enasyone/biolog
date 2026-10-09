namespace Biolog.Features.FingerprintSearch.Domain;

/// <summary>
/// AC04: "No automatic PERSON merge occurs." Mirrors BIO-IDN-005's own <c>DuplicateReviewStatus</c>
/// wire strings exactly (deliberate vocabulary consistency across the platform's two duplicate-
/// review concepts - see this project's csproj remarks for why this story owns its own review
/// queue rather than reaching into BIO-IDN-005's). There is deliberately no "Merged" member on
/// this enum, and no method anywhere in this project performs a PERSON merge - see
/// docs/features/bio-bio-006/README.md "AC04 - no merge exists in this codebase".
/// <see cref="ConfirmedDuplicate"/> is routing information for a separate, wholly manual
/// operational process outside this system's automation, not a trigger this feature fires.
/// </summary>
public enum FingerprintDuplicateReviewStatus
{
    PendingReview,
    ConfirmedDuplicate,
    ConfirmedDistinct,
}

public static class FingerprintDuplicateReviewStatusExtensions
{
    public static string ToWireString(this FingerprintDuplicateReviewStatus status) => status switch
    {
        FingerprintDuplicateReviewStatus.PendingReview => "PENDING_REVIEW",
        FingerprintDuplicateReviewStatus.ConfirmedDuplicate => "CONFIRMED_DUPLICATE",
        FingerprintDuplicateReviewStatus.ConfirmedDistinct => "CONFIRMED_DISTINCT",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "unreachable: exhaustive switch"),
    };
}
