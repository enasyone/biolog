namespace Biolog.Features.EvidenceComparison.Audit;

/// <summary>
/// One append-only audit envelope (ADR-0003 Decision 1 / <c>contracts/schemas/audit-event.schema.json</c>),
/// scoped to this feature's own action. <see cref="Detail"/> deliberately carries only counts and
/// flags about the comparison (field count, mismatch count, review flag, NFC signal), never the
/// compared field values themselves - the same "evidence references, not evidence copies"
/// reasoning (ADR-0003 Decision 5) every prior BIO-DOC-00x evidence type this epic has followed;
/// the actual per-field raw/normalized values stay in the <see cref="CrossComparisonResult"/>
/// this event was written for, not duplicated into the durable audit trail.
/// </summary>
public sealed record EvidenceComparisonAuditEvent
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
