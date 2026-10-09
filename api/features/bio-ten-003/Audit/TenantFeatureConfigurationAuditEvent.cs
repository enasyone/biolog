namespace Biolog.Features.TenantFeatureConfig.Audit;

/// <summary>
/// One append-only audit envelope (ADR-0003 Decision 1 / <c>contracts/schemas/audit-event.schema.json</c>),
/// scoped to this feature's administrative actions. Unlike BIO-TEN-001's own <c>TenantAuditEvent</c>
/// (always <c>TenantId: null</c>, control-plane scoped), <see cref="TenantId"/> here is
/// REQUIRED and always set: every action this feature audits is tenant-plane work - a change
/// to one specific tenant's own feature configuration (ADR-0002 §1.4's complementary case to
/// BIO-TEN-001's). <c>Device</c> is always null - no device-adapter story exists yet for
/// control-plane/tenant-admin operators. No <c>version</c> on the event itself, ever
/// (ADR-0001 §3.3: append-only, never a mutable aggregate) - that is a field of
/// <c>TenantFeatureConfiguration</c>, not of this event.
/// </summary>
public sealed record TenantFeatureConfigurationAuditEvent
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
