namespace Biolog.Features.Attendance.Audit;

/// <summary>
/// One append-only audit envelope (ADR-0003 Decision 1 / <c>contracts/schemas/audit-event.schema.json</c>),
/// scoped to this feature's attendance mutations. A local, right-sized hand-port of the shared
/// schema - not a shared library reference - the same "each .NET feature ports the envelope
/// itself" pattern BIO-IDN-001's own <c>PersonAuditEvent.cs</c> established and every
/// subsequent identity-wave story has followed since (no generated .NET binding of
/// <c>contracts/js/audit.mjs</c> exists yet). <see cref="TenantId"/> is required: attendance is
/// tenant-plane. No <c>version</c> field, ever (ADR-0001 §3.3: append-only, never a mutable
/// aggregate).
/// </summary>
public sealed record AttendanceAuditEvent
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
