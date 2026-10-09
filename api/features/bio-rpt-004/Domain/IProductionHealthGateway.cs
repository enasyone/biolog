namespace Biolog.Features.OperationalDashboard.Domain;

/// <summary>This feature's own whitelisted projection onto BIO-CRD-007's production-job counts (BIO-RPT-004-AC01) - never the full <c>ProductionJob</c> record itself.</summary>
public sealed record ProductionCounts(int Total, int Passed, int Failed, int Rejected, int InProgress, int Cancelled);

/// <summary>Real cross-story dependency (BIO-CRD-007). Implemented by an adapter in <c>BioRpt004Module</c> wrapping BIO-CRD-007's own already-registered, PUBLIC <c>IProductionJobRepository.ListForTenant</c> directly - the exact same already-public method BIO-RPT-002's own <c>ICardProductionGateway</c> independently wraps for a different dimension, no new access grant needed.</summary>
public interface IProductionHealthGateway
{
    Task<ProductionCounts> GetAsync(string tenantId, CancellationToken cancellationToken = default);
}
