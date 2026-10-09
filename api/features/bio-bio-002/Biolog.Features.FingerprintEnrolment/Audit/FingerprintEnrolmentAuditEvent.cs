namespace Biolog.Features.FingerprintEnrolment.Audit;

/// <summary>BIO-BIO-002-AC05: "Enrolment is audited." One immutable record of a session mutation.
/// Never carries a biometric payload or template bytes - only metadata about what happened
/// (mirrors BIO-IDN-001's "never place a PII/sensitive VALUE in Detail" audit-privacy
/// discipline).</summary>
public sealed record FingerprintEnrolmentAuditEvent
{
    public required string EventId { get; init; }
    public required string TenantId { get; init; }
    public required string SessionId { get; init; }
    public required string PersonId { get; init; }
    public required string Action { get; init; }
    public string? FingerPosition { get; init; }
    public string? Reason { get; init; }
    public required string ActorId { get; init; }
    public required string OccurredAtUtc { get; init; }
}
