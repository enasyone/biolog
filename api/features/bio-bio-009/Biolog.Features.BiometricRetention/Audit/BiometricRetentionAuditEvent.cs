namespace Biolog.Features.BiometricRetention.Audit;

/// <summary>BIO-BIO-009-AC02: "Template deletion is audited." One immutable record of a retention
/// or deletion mutation - mirrors BIO-BIO-002's own <c>FingerprintEnrolmentAuditEvent</c> shape.
/// Never carries a biometric payload or template bytes, and never carries an actual raw image -
/// only metadata about what happened (ADR-0003 Decision 3: a <c>secret</c>-classified value is
/// never copied into an audit event in any form).</summary>
public sealed record BiometricRetentionAuditEvent
{
    public required string EventId { get; init; }
    public required string TenantId { get; init; }
    public required string Action { get; init; }

    /// <summary>The record/category this event is about - a session id, reference id, person id
    /// or category name, depending on <see cref="Action"/>. Never a template or image byte
    /// payload.</summary>
    public required string TargetId { get; init; }

    public string? Reason { get; init; }
    public required string ActorId { get; init; }
    public required string OccurredAtUtc { get; init; }
}
