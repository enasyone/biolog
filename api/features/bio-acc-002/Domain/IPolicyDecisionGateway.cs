namespace Biolog.Features.AccessDecisions.Domain;

/// <summary>
/// One policy-evaluation request, shaped as plain primitives so this gateway boundary never
/// leaks BIO-ACC-001's own internal domain types across the assembly seam (the same "gateway
/// returns primitives/small local records, never the sibling's own domain type" discipline
/// <c>ITransportRegistryGateway</c> already established for BIO-TRN-001).
/// <see cref="PresentedAssuranceLevel"/> is THIS feature's own derived value
/// (<see cref="AccessDecisionCombiner.DeriveAssuranceLevel"/>, AC01) - never itself a decision.
/// </summary>
public sealed record PolicyDecisionQuery
{
    public required string PolicyKey { get; init; }
    public required string PersonId { get; init; }
    public string? ProfileKey { get; init; }
    public required string CredentialClass { get; init; }
    public required string LocationId { get; init; }
    public required string OccurredAtUtc { get; init; }
    public required string PresentedCardStatus { get; init; }
    public required string PresentedAssuranceLevel { get; init; }
}

/// <summary>
/// BIO-ACC-001's own evaluation outcome, mirrored as plain primitives. AC02's suspended/revoked
/// deny-precedence and AC03's policy version/reason code all flow through unchanged from
/// BIO-ACC-001's own <c>PolicyEvaluator</c> - this feature never re-derives or second-guesses that
/// logic, only orchestrates the evidence that feeds it (AC01).
/// </summary>
public sealed record PolicyDecisionResult
{
    public required AccessDecisionOutcome Decision { get; init; }
    public required string ReasonCode { get; init; }
    public required string PolicyKey { get; init; }
    public int? PolicyVersionNumber { get; init; }
    public int? MatchedRuleId { get; init; }
}

/// <summary>
/// Cross-story seam onto BIO-ACC-001's own already-registered, ADR-0006-gated
/// <c>PolicyEvaluationService.EvaluateAsync</c> - implemented by an adapter in
/// <c>BioAcc002Module.cs</c> that builds BIO-ACC-001's own wire request DTO and maps its
/// <c>PolicyEvaluationResult</c> back to this feature's own primitives. The caller's actor and its
/// permission (<c>access_policy.evaluate</c>) are enforced ENTIRELY by BIO-ACC-001's own service -
/// this gateway does not, and must not, duplicate that check.
/// </summary>
public interface IPolicyDecisionGateway
{
    Task<PolicyDecisionResult> EvaluateAsync(
        PolicyDecisionQuery query, Audit.AuditActor actor, string correlationId, CancellationToken cancellationToken = default);
}
