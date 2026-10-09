namespace Biolog.Features.PersonLifecycle.Audit;

/// <summary>
/// One append-only audit envelope (ADR-0003 Decision 1 / <c>contracts/schemas/audit-event.schema.json</c>),
/// scoped to this feature's own supplementary lifecycle-transition record. This is NOT the
/// authoritative <c>person.status_change</c> audit event - that already lives in BIO-IDN-001's
/// own audit trail, written inside <c>PersonService.UpdateStatusAsync</c> before this project's
/// own gateway call even returns. This is BIO-IDN-004-AC04's own traceable record of what THIS
/// feature did (which named transition, why, by whom), queryable independently of BIO-IDN-001's
/// trail and never deleted when a person reaches ARCHIVED.
/// </summary>
public sealed record PersonLifecycleAuditEvent
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
