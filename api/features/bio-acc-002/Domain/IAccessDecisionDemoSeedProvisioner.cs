namespace Biolog.Features.AccessDecisions.Domain;

public sealed record DemoPolicySnapshot
{
    public required string PolicyKey { get; init; }
    public required int PublishedVersionNumber { get; init; }
    public required int RuleCount { get; init; }
}

/// <summary>
/// AC05's demo-only seam onto BIO-ACC-001's own policy aggregate/evaluator, registered Singleton
/// (mirrors <c>ITransportDemoSeedProvisioner</c>'s own captive-dependency reasoning in
/// <c>BioTrn002Module.cs</c> - deliberately a SEPARATE interface from the Scoped, RBAC-gated
/// <see cref="IPolicyDecisionGateway"/>). <see cref="EvaluateDemoPolicyAsync"/>'s adapter calls
/// BIO-ACC-001's own PURE <c>PolicyEvaluator.Evaluate</c> directly with a fixed
/// <c>IdentityStatus.Active</c>, bypassing both the live PersonService identity lookup and the
/// <c>access_policy.evaluate</c> RBAC/provenance gate entirely - a headerless demo POST resolves
/// neither a live tenant-scoped PersonService nor a provisioned actor, the same reason every other
/// demo bridge in this codebase bypasses its own tenant-scoped service and calls domain statics
/// directly instead. See docs/features/bio-acc-002/README.md "AC05 web wiring" for the full record.
/// </summary>
public interface IAccessDecisionDemoSeedProvisioner
{
    /// <summary>Finds the fixed demo policy (creating and publishing it via BIO-ACC-001's own real aggregate the first time either bridge action needs it) or returns its existing snapshot - idempotent, safe to call from either action, in either order.</summary>
    Task<DemoPolicySnapshot> EnsureDemoPolicyAsync(string tenantId, CancellationToken cancellationToken = default);

    Task<PolicyDecisionResult> EvaluateDemoPolicyAsync(string tenantId, string policyKey, PolicyDecisionQuery query, CancellationToken cancellationToken = default);
}
