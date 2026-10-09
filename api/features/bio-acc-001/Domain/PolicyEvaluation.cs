namespace Biolog.Features.AccessPolicy.Domain;

/// <summary>
/// One evaluation request (BIO-ACC-001-AC04: "policy evaluation is auditable and testable").
/// <see cref="PersonId"/>'s identity status is resolved LIVE via <see cref="IIdentityStatusGateway"/>
/// (see that interface's header for why); every other dimension is supplied directly by the
/// caller - the actual access-control system (a reader/gate integration) that already resolved
/// its own credential class, location and card status independently, since this story has no
/// reachable system of record for any of those within its declared dependency graph.
/// </summary>
public sealed record PolicyEvaluationContext
{
    public required string PersonId { get; init; }
    public string? ProfileKey { get; init; }
    public required string CredentialClass { get; init; }
    public required string LocationId { get; init; }
    public required DateTime OccurredAtUtc { get; init; }
    public required CardStatus PresentedCardStatus { get; init; }
    public required AssuranceLevel PresentedAssuranceLevel { get; init; }
}

public enum PolicyEvaluationDecision
{
    Allow,
    Deny,
}

public static class PolicyEvaluationDecisionExtensions
{
    public static string ToWireString(this PolicyEvaluationDecision decision) => decision switch
    {
        PolicyEvaluationDecision.Allow => "ALLOW",
        PolicyEvaluationDecision.Deny => "DENY",
        _ => throw new ArgumentOutOfRangeException(nameof(decision), decision, "unreachable: exhaustive switch"),
    };
}

/// <summary>
/// AC03/AC04's evaluation outcome - always a decision, never a 404: "no policy configured" and
/// "no matching allow rule" are both meaningful DENY answers for a physical-security decision,
/// not error conditions (fail-closed, per <see cref="PolicyEvaluator"/>'s own header).
/// </summary>
public sealed record PolicyEvaluationResult
{
    public required PolicyEvaluationDecision Decision { get; init; }
    public required string ReasonCode { get; init; }
    public required string PolicyKey { get; init; }
    public int? PolicyVersionNumber { get; init; }
    public int? MatchedRuleId { get; init; }
}

/// <summary>
/// AC03's real enforcement point: "deny precedence is explicit". Collects every rule that
/// structurally matches the context (WHO+CREDENTIAL+WHERE+WHEN+STATUS), regardless of its
/// position in the rule list, and applies effects in a fixed, explicit order:
///
/// <list type="number">
/// <item>Any matching DENY rule wins outright - evaluated FIRST, before any ALLOW rule is even
/// considered for assurance sufficiency. A tenant administrator who writes a DENY rule can
/// therefore always trust it overrides every ALLOW rule that also matches, unconditionally.</item>
/// <item>Otherwise, an ALLOW rule wins only if the presented assurance level satisfies its own
/// <see cref="PolicyRule.RequiredAssuranceLevel"/> - a matching ALLOW rule the caller's assurance
/// does not satisfy is reported as its own distinct <c>insufficient_assurance</c> reason, not
/// silently folded into the generic no-match case.</item>
/// <item>Otherwise, DENY by default - no rule matched, or every matching ALLOW rule's assurance
/// requirement went unmet. A policy that defines no rule for a given request denies it, rather
/// than defaulting open.</item>
/// </list>
/// </summary>
public static class PolicyEvaluator
{
    public static PolicyEvaluationResult Evaluate(
        string policyKey, AccessPolicyVersionContent? publishedVersion, PolicyEvaluationContext context, IdentityStatus? identityStatus)
    {
        if (publishedVersion is null)
        {
            return new PolicyEvaluationResult { Decision = PolicyEvaluationDecision.Deny, ReasonCode = "no_published_policy", PolicyKey = policyKey };
        }

        if (identityStatus is not IdentityStatus resolvedIdentityStatus)
        {
            return new PolicyEvaluationResult
            {
                Decision = PolicyEvaluationDecision.Deny, ReasonCode = "identity_status_unresolved", PolicyKey = policyKey, PolicyVersionNumber = publishedVersion.VersionNumber,
            };
        }

        var matches = publishedVersion.Rules
            .Where(r => r.StructurallyMatches(context, context.ProfileKey, resolvedIdentityStatus, context.PresentedCardStatus))
            .ToList();

        var denyMatch = matches.FirstOrDefault(r => r.Effect == PolicyRuleEffect.Deny);
        if (denyMatch is not null)
        {
            return new PolicyEvaluationResult
            {
                Decision = PolicyEvaluationDecision.Deny, ReasonCode = "deny_rule_matched", PolicyKey = policyKey,
                PolicyVersionNumber = publishedVersion.VersionNumber, MatchedRuleId = denyMatch.RuleId,
            };
        }

        var allowMatches = matches.Where(r => r.Effect == PolicyRuleEffect.Allow).ToList();
        var sufficientAllow = allowMatches
            .Where(r => context.PresentedAssuranceLevel.Satisfies(r.RequiredAssuranceLevel))
            .OrderByDescending(r => r.RequiredAssuranceLevel)
            .FirstOrDefault();

        if (sufficientAllow is not null)
        {
            return new PolicyEvaluationResult
            {
                Decision = PolicyEvaluationDecision.Allow, ReasonCode = "allow_rule_matched", PolicyKey = policyKey,
                PolicyVersionNumber = publishedVersion.VersionNumber, MatchedRuleId = sufficientAllow.RuleId,
            };
        }

        if (allowMatches.Count > 0)
        {
            return new PolicyEvaluationResult
            {
                Decision = PolicyEvaluationDecision.Deny, ReasonCode = "insufficient_assurance", PolicyKey = policyKey,
                PolicyVersionNumber = publishedVersion.VersionNumber, MatchedRuleId = allowMatches[0].RuleId,
            };
        }

        return new PolicyEvaluationResult
        {
            Decision = PolicyEvaluationDecision.Deny, ReasonCode = "no_matching_rule", PolicyKey = policyKey, PolicyVersionNumber = publishedVersion.VersionNumber,
        };
    }
}
