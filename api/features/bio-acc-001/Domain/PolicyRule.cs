namespace Biolog.Features.AccessPolicy.Domain;

/// <summary>
/// BIO-ACC-001-AC03: "deny precedence is explicit" - a per-rule field, not an implicit
/// consequence of list order. <see cref="PolicyEvaluation.PolicyEvaluator"/> collects every
/// structurally matching rule regardless of position, then applies <see cref="Deny"/> before ever
/// considering an <see cref="Allow"/> - see that class's own header.
/// </summary>
public enum PolicyRuleEffect
{
    Allow,
    Deny,
}

public static class PolicyRuleEffectExtensions
{
    public static string ToWireString(this PolicyRuleEffect effect) => effect switch
    {
        PolicyRuleEffect.Allow => "ALLOW",
        PolicyRuleEffect.Deny => "DENY",
        _ => throw new ArgumentOutOfRangeException(nameof(effect), effect, "unreachable: exhaustive switch"),
    };

    public static bool TryParse(string? value, out PolicyRuleEffect effect)
    {
        switch (value)
        {
            case "ALLOW": effect = PolicyRuleEffect.Allow; return true;
            case "DENY": effect = PolicyRuleEffect.Deny; return true;
            default: effect = default; return false;
        }
    }
}

/// <summary>
/// One WHO+CREDENTIAL+WHERE+WHEN+STATUS+ASSURANCE predicate (BIO-ACC-001-AC01), immutable once
/// constructed. <see cref="RuleId"/> is 1-based and unique within its owning
/// <see cref="AccessPolicyVersionContent"/>, for audit/reference only - it plays no role in
/// evaluation precedence (AC03's deny precedence is effect-based, never order-based).
/// </summary>
public sealed record PolicyRule
{
    public required int RuleId { get; init; }
    public required PolicyRuleEffect Effect { get; init; }
    public required SubjectMatch Subject { get; init; }
    public required CredentialClassMatch Credential { get; init; }
    public required LocationMatch Location { get; init; }
    public required ScheduleMatch Schedule { get; init; }
    public required IdentityStatusMatch IdentityStatus { get; init; }
    public required CardStatusMatch CardStatus { get; init; }
    public required AssuranceLevel RequiredAssuranceLevel { get; init; }

    /// <summary>Structural match only (WHO+CREDENTIAL+WHERE+WHEN+STATUS) - deliberately excludes assurance, which <see cref="PolicyEvaluation.PolicyEvaluator"/> evaluates as a separate, explicit step so an "insufficient assurance" outcome is distinguishable from "no matching rule at all".</summary>
    public bool StructurallyMatches(PolicyEvaluationContext context, string? profileKey, IdentityStatus identityStatus, CardStatus cardStatus) =>
        Subject.Matches(context.PersonId, profileKey)
        && Credential.Matches(context.CredentialClass)
        && Location.Matches(context.LocationId)
        && Schedule.Matches(context.OccurredAtUtc)
        && IdentityStatus.Matches(identityStatus)
        && CardStatus.Matches(cardStatus);
}
