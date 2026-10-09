namespace Biolog.Features.TenantDomainRegistry.Audit;

/// <summary>
/// One append-only audit envelope (ADR-0003 Decision 1 / <c>contracts/schemas/audit-event.schema.json</c>),
/// scoped to this feature's administrative actions. <c>TenantId</c> is always null: domain
/// mapping is control-plane work exactly like the tenant registry itself (ADR-0002 §1.4
/// explicitly names "the tenant registry, domains, licensing, routing metadata" together as
/// control-plane), and the schema describes null there as meaning exactly that ("Null only
/// for control-plane actions") - which tenant a given domain belongs to is carried instead as
/// a normal, internal-classified <c>Detail</c> field, not the envelope's own tenant context.
/// <c>Device</c> is always null - no admin console or device-adapter story exists yet for
/// control-plane operators. No <c>version</c>, ever (ADR-0001 §3.3: append-only, never a
/// mutable aggregate).
/// </summary>
public sealed record TenantDomainAuditEvent
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
