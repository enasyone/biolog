namespace Biolog.Api.Features.DeviceCertification.Audit;

/// <summary>An actor identity per ADR-0003 Decision 1, mirroring
/// <see cref="Biolog.Api.Audit.AuditActor"/>'s shape exactly.</summary>
public sealed record CertificationAuditActor(string Type, string Id);

/// <summary>What an audited action acted on.</summary>
public sealed record CertificationAuditTarget(string Type, string Id);

/// <summary>The three outcomes ADR-0003 Decision 1 requires every audit event to
/// carry.</summary>
public enum CertificationAuditOutcome
{
    Allowed,
    Denied,
    Error,
}

/// <summary>
/// The append-only audit envelope for certification matrix changes (ADR-0003 Decision 1;
/// BIO-DEV-002-AC04). A dedicated record rather than a reuse of
/// <see cref="Biolog.Api.Audit.AuditEvent"/>: this feature's <see cref="TenantId"/> must
/// be nullable (a platform-wide default entry has no tenant to attribute it to - ADR-0003's
/// schema: "Null only for control-plane actions", the same shape BIO-TEN-001's own
/// <c>TenantAuditEvent.TenantId</c> already uses), which
/// <see cref="Biolog.Api.Audit.AuditEvent"/>'s <c>required string TenantId</c> does not
/// allow without widening a type <see cref="Features.DeviceRegistry.DeviceService"/> also
/// depends on. Every sibling story with its own audit shape in this codebase (BIO-TEN-001's
/// <c>TenantAuditEvent</c>, BIO-TEN-002's <c>TenantDomainAuditEvent</c>) makes the same
/// choice for the same reason: a dedicated envelope per story, not a shared mutable one.
/// </summary>
public sealed record CertificationAuditEvent
{
    public required string EventId { get; init; }
    public required int SchemaVersion { get; init; } = 1;

    /// <summary><c>null</c> for a change to the platform-wide default entry (control-plane,
    /// "As platform operations"); a real tenant id for a change to that tenant's own
    /// override (BIO-DEV-002-AC03).</summary>
    public string? TenantId { get; init; }
    public required CertificationAuditActor Actor { get; init; }
    public required string Action { get; init; }
    public required CertificationAuditTarget Target { get; init; }
    public required CertificationAuditOutcome Outcome { get; init; }
    public required string ReasonCode { get; init; }
    public required string CorrelationId { get; init; }
    public string? CausationId { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }
    public required DateTimeOffset ReceivedAt { get; init; }
    public required string Classification { get; init; }
}
