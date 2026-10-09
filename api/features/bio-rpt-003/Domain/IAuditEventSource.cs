namespace Biolog.Features.AuditInvestigation.Domain;

/// <summary>
/// A read-only fan-in seam: one already-accepted feature's own audit trail, normalized into this
/// story's canonical shape. Implemented entirely by adapters in
/// <c>api/Biolog.Api/Modules/BioRpt003Module.cs</c> (the one place allowed to see every feature at
/// once) - this interface itself never references another feature's types, keeping the one-way
/// dependency rule intact. Genuinely open for extension: any future module can register another
/// <see cref="IAuditEventSource"/> into the same DI collection without touching this project's own
/// files at all.
/// </summary>
public interface IAuditEventSource
{
    /// <summary>Stable name for provenance/debugging (<c>NormalizedAuditRecord.SourceFeature</c>), e.g. <c>"rbac"</c>.</summary>
    string SourceFeature { get; }

    IReadOnlyList<NormalizedAuditRecord> ListForTenant(string tenantId);
}
