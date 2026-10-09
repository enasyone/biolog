namespace Biolog.Api.Audit;

/// <summary>An actor identity per ADR-0003 Decision 1: a structured value, never a bare
/// name, so the user "sync-worker" and the service account "sync-worker" stay
/// distinguishable.</summary>
public sealed record AuditActor(string Type, string Id);

/// <summary>What an audited action acted on.</summary>
public sealed record AuditTarget(string Type, string Id);

/// <summary>
/// The append-only audit envelope (ADR-0003 Decision 1). This is a minimal .NET port
/// covering the fields BIO-DEV-001 actually populates - it does not implement ADR-0003's
/// general allowlist/redaction projection engine (Decision 3) or the idempotency dedup
/// (Decision 4).
///
/// <b>SY-OPS002-06 (Kemi's ruling, 2026-10-07): extended with <see cref="Device"/> and
/// <see cref="Detail"/>, both optional/nullable and backward compatible - every existing
/// caller (<c>DeviceService</c>, <c>DeviceCertificationService</c>) is unaffected.</b> Added
/// so <c>DemoSessionContextAuditEndpoints</c> (BIO-TEN-002) could reuse this shared envelope
/// instead of a bespoke per-feature type for a case with no classified value to protect and
/// only non-sensitive internal technical metadata to carry (the same split
/// <c>PhotoQualityAuditEventBuilder</c>/<c>PhotoAuditEventBuilder</c> already draw for their
/// OWN bespoke, feature-level event types, applied here for the first time to this shared
/// one) - <see cref="Device"/> mirrors those event types' own <c>Device</c> field shape
/// (always <c>null</c> for a non-device-originated action), and <see cref="Detail"/> is an
/// ALLOWLIST dict for exactly that kind of metadata, never a classified field value: a
/// caller choosing to put a classified value in <see cref="Detail"/> is a caller bypassing
/// the discipline this field name signals, not something this minimal envelope's own
/// (deliberately absent) redaction engine can stop - the full allowlist/redaction PROJECTION
/// engine (Decision 3) still belongs to whichever story actually needs classified-value audit
/// fields, per this type's own original note; this extension does not change that.
/// </summary>
public sealed record AuditEvent
{
    public required string EventId { get; init; }
    public required int SchemaVersion { get; init; } = 1;
    public required string TenantId { get; init; }
    public required AuditActor Actor { get; init; }
    public object? Device { get; init; }
    public required string Action { get; init; }
    public required AuditTarget Target { get; init; }
    public required AuditOutcome Outcome { get; init; }
    public required string ReasonCode { get; init; }
    public required string CorrelationId { get; init; }
    public string? CausationId { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }
    public required DateTimeOffset ReceivedAt { get; init; }
    public required string Classification { get; init; }
    public IReadOnlyDictionary<string, object?> Detail { get; init; } = new Dictionary<string, object?>();
}
