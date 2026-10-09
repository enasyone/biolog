namespace Biolog.Features.AccessDecisions.Domain;

/// <summary>
/// Mirrors BIO-ACC-001's own <c>Domain.CardStatus</c> wire strings exactly (itself mirroring
/// BIO-CRD-005's <c>CardIssueStatus</c>). Neither BIO-CRD-005 nor a live card-status lookup is a
/// declared dependency of this story (only BIO-ACC-001, BIO-BIO-004, BIO-BIO-005 are) - the same
/// "caller-supplied evaluation input, not a gateway-resolved fact" boundary BIO-ACC-001's own
/// <c>PolicyEvaluationContext.PresentedCardStatus</c> already established, re-derived here rather
/// than assumed. This exists so the request wire shape and the passthrough into
/// <see cref="IPolicyDecisionGateway"/> both validate against the exact same enum.
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
