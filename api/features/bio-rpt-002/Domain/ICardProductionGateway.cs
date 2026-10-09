namespace Biolog.Features.ReportBuilder.Domain;

/// <summary>One production job's projection onto this feature's own whitelisted CardProduction dimensions (BIO-RPT-002-AC01) - never the full BIO-CRD-007 <c>ProductionJob</c> record itself, the same "narrow gateway interface this project defines" precedent every sibling story's own cross-feature read establishes.</summary>
public sealed record CardProductionRecord(string Status, string Source, string CardClass);

/// <summary>Real cross-story dependency (BIO-CRD-007). Implemented by an adapter in <c>BioRpt002Module</c> wrapping BIO-CRD-007's own already-registered, PUBLIC <c>IProductionJobRepository.ListForTenant</c> directly.</summary>
public interface ICardProductionGateway
{
    Task<IReadOnlyList<CardProductionRecord>> ListAsync(string tenantId, CancellationToken cancellationToken = default);
}
