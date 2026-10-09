namespace Biolog.Features.AuditInvestigation.Audit;

/// <summary>
/// One append-only audit envelope (ADR-0003 Decision 1), scoped to THIS feature's own actions
/// (searching/exporting an investigation view) - not the records being investigated, which are
/// each already an envelope of their own SOURCE feature's shape (<c>Domain.NormalizedAuditRecord</c>).
/// BIO-RPT-003-AC03's "export is ... audited" is this trail. No <c>version</c>, ever (ADR-0001 §3.3).
/// </summary>
public sealed record AuditInvestigationAuditEvent
{
    public required string EventId { get; init; }
    public int SchemaVersion { get; init; } = 1;
    public required string TenantId { get; init; }
    public required AuditActor Actor { get; init; }
    public object? Device { get; init; }
    public required string Action { get; init; }
    public required AuditTarget Target { get; init; }
    public required AuditOutcome Outcome { get; init; }
    public required string ReasonCode { get; init; }
    public required string CorrelationId { get; init; }
    public string? CausationId { get; init; }
    public required string OccurredAt { get; init; }
    public required string ReceivedAt { get; init; }
    public required string Classification { get; init; }
    public IReadOnlyList<string> Withheld { get; init; } = [];
    public IReadOnlyDictionary<string, object?> Detail { get; init; } = new Dictionary<string, object?>();
}
