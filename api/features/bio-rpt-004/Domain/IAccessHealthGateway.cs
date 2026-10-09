namespace Biolog.Features.OperationalDashboard.Domain;

/// <summary>This feature's own whitelisted projection onto BIO-ACC-002's access-decision counts (BIO-RPT-004-AC01) - never the full <c>AccessDecisionEvent</c> record itself.</summary>
public sealed record AccessCounts(int Total, int Allowed, int Denied);

/// <summary>Real cross-story dependency (BIO-ACC-002). Implemented by an adapter in <c>BioRpt004Module</c> wrapping BIO-ACC-002's own already-registered <c>IAccessDecisionEventRepository.ListForTenantAsync</c> directly - that interface is <c>internal</c>, not public, but already grants <c>Biolog.Api</c> access via <c>InternalsVisibleTo</c>, the same access that story's own module registration already uses.</summary>
public interface IAccessHealthGateway
{
    Task<AccessCounts> GetAsync(string tenantId, CancellationToken cancellationToken = default);
}
