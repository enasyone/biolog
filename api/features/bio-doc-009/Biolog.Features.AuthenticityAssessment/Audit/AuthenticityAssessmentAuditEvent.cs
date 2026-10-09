namespace Biolog.Features.AuthenticityAssessment.Audit;

/// <summary>
/// One append-only audit envelope (ADR-0003 Decision 1 / <c>contracts/schemas/audit-event.schema.json</c>),
/// scoped to this feature's own action. <see cref="Detail"/> carries only the disposition, check
/// count/outcomes summary, and AC04's version fields - never the underlying evidence values
/// themselves (ADR-0003 Decision 5, the same "evidence references, not evidence copies" reasoning
/// every prior BIO-DOC-00x audit event in this epic follows); the actual per-check detail text
/// stays on the <see cref="Model.AuthenticityAssessmentResult"/> this event was written for.
/// </summary>
public sealed record AuthenticityAssessmentAuditEvent
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
