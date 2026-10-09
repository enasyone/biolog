namespace Biolog.Features.CardTemplates.Audit;

/// <summary>
/// One append-only audit envelope (ADR-0003 Decision 1), scoped to this feature's template
/// lifecycle transitions (create/edit/submit/validate/reject/publish/retire) - the story title's
/// own "provenance" requirement. No <c>version</c>, ever (ADR-0001 §3.3).
/// </summary>
public sealed record CardTemplateAuditEvent
{
    public required string EventId { get; init; }
    public int SchemaVersion { get; init; } = 1;
    public required string TenantId { get; init; }
    public required AuditActor Actor { get; init; }
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
