using Biolog.Features.AuditInvestigation.Audit;

namespace Biolog.Features.AuditInvestigation.Domain;

/// <summary>
/// <c>{ device_id, workstation }</c> (ADR-0003 Decision 1). Every existing audit-event builder in
/// this codebase sets its Device to <c>null</c> today (verified directly: RbacAuditEventBuilder,
/// ProductionAuditEventBuilder, ReportAuditEventBuilder and CardTemplateAuditEvent's own producer
/// all pass/hold <c>null</c>) - "no device" and "nobody recorded the device" stay different
/// answers per that Decision, and this is honestly the first case, not the second, for every
/// source wired today. See docs/features/bio-rpt-003/README.md for how the AC01 device filter is
/// still verified against a synthetic fixture.
/// </summary>
public sealed record AuditDeviceRef(string? DeviceId, string? Workstation);

/// <summary>
/// The canonical read-model an investigation view is built from - one shape for every source
/// feature's own audit envelope (ADR-0003 Decision 1), with two additions of this story's own:
/// <see cref="SourceFeature"/> (provenance: which feature's own sink this came from) and
/// <see cref="PersonId"/> (nullable - populated only when the source adapter can genuinely name
/// one; BIO-RPT-003-AC01's "person" filter). <see cref="Classification"/>/<see cref="Withheld"/>/
/// <see cref="Detail"/> are exactly what the PRODUCING feature already decided at write time -
/// this record never reaches past that into a raw, un-redacted source object (AC04's first half);
/// <see cref="AuditViewRedaction"/> is the second half, applied at read time per viewer.
/// </summary>
public sealed record NormalizedAuditRecord
{
    public required string SourceFeature { get; init; }
    public required string EventId { get; init; }
    public int SchemaVersion { get; init; } = 1;
    public required string TenantId { get; init; }
    public required AuditActor Actor { get; init; }
    public AuditDeviceRef? Device { get; init; }
    public string? PersonId { get; init; }
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
