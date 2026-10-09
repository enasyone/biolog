namespace Biolog.Features.CardIssuance.Audit;

/// <summary>
/// One append-only audit envelope (ADR-0003 Decision 1 / <c>contracts/schemas/audit-event.schema.json</c>),
/// scoped to this feature's card-issue mutations. <see cref="TenantId"/> is required (tenant-
/// plane). No <c>version</c>, ever (ADR-0001 §3.3: audit events are append-only, never a
/// mutable aggregate - this is a separate, write-only trail alongside the mutable
/// <see cref="Domain.CardIssue"/> aggregate itself, not a replacement for its own CAS version).
/// </summary>
public sealed record CardIssueAuditEvent
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
