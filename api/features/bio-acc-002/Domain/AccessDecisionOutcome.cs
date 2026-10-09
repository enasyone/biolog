namespace Biolog.Features.AccessDecisions.Domain;

/// <summary>Mirrors BIO-ACC-001's own <c>PolicyEvaluationDecision</c> - this feature never mints its own allow/deny, only carries BIO-ACC-001's own decision through (AC01).</summary>
public enum AccessDecisionOutcome
{
    Allow,
    Deny,
}

public static class AccessDecisionOutcomeExtensions
{
    public static string ToWireString(this AccessDecisionOutcome decision) => decision switch
    {
        AccessDecisionOutcome.Allow => "ALLOW",
        AccessDecisionOutcome.Deny => "DENY",
        _ => throw new ArgumentOutOfRangeException(nameof(decision), decision, "unreachable: exhaustive switch"),
    };
}
