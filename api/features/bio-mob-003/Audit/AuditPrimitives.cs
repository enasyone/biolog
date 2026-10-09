namespace Biolog.Features.OfflineSync.Audit;

/// <summary>Mirrors <c>ACTOR_TYPES</c> in <c>contracts/js/audit.mjs</c> (ADR-0003).</summary>
public enum ActorType
{
    User,
    ServiceAccount,
    Device,
    System,
    Anonymous,
    Unknown,
}

public sealed record AuditActor(ActorType Type, string Id);

public sealed record AuditTarget(string Type, string? Id);

/// <summary>Mirrors <c>OUTCOMES</c> in <c>contracts/js/audit.mjs</c>.</summary>
public enum AuditOutcome
{
    Allowed,
    Denied,
    Error,
}

public static class AuditOutcomeExtensions
{
    public static string ToWireString(this AuditOutcome outcome) => outcome switch
    {
        AuditOutcome.Allowed => "ALLOWED",
        AuditOutcome.Denied => "DENIED",
        AuditOutcome.Error => "ERROR",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "unreachable: exhaustive switch"),
    };
}

/// <summary>ADR-0003: one durable audit record per mutating request this feature handles.</summary>
public sealed record OfflineSyncAuditEvent(
    string EventId,
    string TenantId,
    AuditActor Actor,
    string Action,
    AuditTarget Target,
    AuditOutcome Outcome,
    string ReasonCode,
    string CorrelationId,
    DateTimeOffset OccurredAt);
