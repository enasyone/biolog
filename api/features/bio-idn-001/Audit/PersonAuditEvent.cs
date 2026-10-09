namespace Biolog.Features.PersonRegistry.Audit;

/// <summary>
/// One append-only audit envelope (ADR-0003 Decision 1 / <c>contracts/schemas/audit-event.schema.json</c>),
/// scoped to this feature's PERSON mutations. Unlike BIO-TEN-002's <c>TenantDomainAuditEvent</c>
/// (control-plane, <c>TenantId</c> always null), <see cref="TenantId"/> here is REQUIRED:
/// PERSON is tenant-plane (ADR-0001's identifier registry), and the schema itself describes
/// null as meaning "only for control-plane actions." <see cref="Device"/> is always null - no
/// device/reader/kiosk enrolment surface exists in this story; a future device-driven
/// enrolment path sets it without changing this envelope's shape. No <c>version</c>, ever
/// (ADR-0001 §3.3: append-only, never a mutable aggregate).
/// </summary>
public sealed record PersonAuditEvent
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
