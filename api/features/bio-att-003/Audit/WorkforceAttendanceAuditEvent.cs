namespace Biolog.Features.WorkforceAttendance.Audit;

/// <summary>
/// One append-only audit envelope (ADR-0003 Decision 1 / <c>contracts/schemas/audit-event.schema.json</c>),
/// scoped to this feature's workforce-attendance mutations. A local, right-sized hand-port of
/// the shared schema, copied from BIO-ATT-001's identically-shaped audit event per this
/// codebase's "each .NET feature ports the envelope itself" convention. <see cref="TenantId"/>
/// is required: workforce attendance is tenant-plane. No <c>version</c> field, ever (ADR-0001
/// §3.3: append-only, never a mutable aggregate).
/// </summary>
public sealed record WorkforceAttendanceAuditEvent
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
