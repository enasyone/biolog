namespace Biolog.Features.AccessPolicy.Domain;

/// <summary>
/// Mirrors BIO-CRD-005's own <c>Domain.CardIssueStatus</c> wire strings. BIO-CRD-005 is not a
/// declared dependency of this story (only BIO-IDN-006 and BIO-ARC-006 are) and this project
/// takes no runtime gateway to it - see <see cref="PolicyEvaluationContext.PresentedCardStatus"/>'s
/// header for why card status is a caller-supplied evaluation input here, not a live-resolved
/// gateway fact the way <see cref="IdentityStatus"/> is. This enum exists only so a policy rule
/// can express "a card in this status", per AC01.
/// </summary>
public enum CardStatus
{
    Issued,
    Active,
    Deactivated,
}

public static class CardStatusExtensions
{
    public static string ToWireString(this CardStatus status) => status switch
    {
        CardStatus.Issued => "issued",
        CardStatus.Active => "active",
        CardStatus.Deactivated => "deactivated",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "unreachable: exhaustive switch"),
    };

    public static bool TryParse(string? value, out CardStatus status)
    {
        switch (value)
        {
            case "issued": status = CardStatus.Issued; return true;
            case "active": status = CardStatus.Active; return true;
            case "deactivated": status = CardStatus.Deactivated; return true;
            default: status = default; return false;
        }
    }
}
