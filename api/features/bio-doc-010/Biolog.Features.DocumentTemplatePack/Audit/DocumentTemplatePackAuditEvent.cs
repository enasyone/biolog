namespace Biolog.Features.DocumentTemplatePack.Audit;

/// <summary>
/// One append-only audit envelope (ADR-0003 Decision 1 / <c>contracts/schemas/audit-event.schema.json</c>),
/// scoped to this feature's own two actions (pack published, verification recorded).
/// <see cref="Detail"/> carries only identifiers/counts/versions - never raw observed field values
/// (ADR-0003 Decision 5's "evidence references, not evidence copies").
/// </summary>
public sealed record DocumentTemplatePackAuditEvent
{
    public required string EventId { get; init; }
    public int SchemaVersion { get; init; } = 1;
    public string? TenantId { get; init; }
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
