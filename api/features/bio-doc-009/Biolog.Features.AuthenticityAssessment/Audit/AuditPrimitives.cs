namespace Biolog.Features.AuthenticityAssessment.Audit;

/// <summary>Own local copy of the identically-named primitives every audited BIOLOG feature
/// carries (e.g. <c>Biolog.Features.EvidenceComparison.Audit.AuditPrimitives</c>) - mirrors
/// <c>ACTOR_TYPES</c>/<c>OUTCOMES</c> in <c>contracts/js/audit.mjs</c> (ADR-0003).</summary>
public enum ActorType
{
    User,
    ServiceAccount,
    Device,
    System,
    Anonymous,
    Unknown,
}

public static class ActorTypeExtensions
{
    public static string ToWireString(this ActorType type) => type switch
    {
        ActorType.User => "user",
        ActorType.ServiceAccount => "service_account",
        ActorType.Device => "device",
        ActorType.System => "system",
        ActorType.Anonymous => "anonymous",
        ActorType.Unknown => "unknown",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "unreachable: exhaustive switch"),
    };
}

/// <summary>ADR-0003: "structured, never a bare string: a name alone cannot tell a user from a service account of the same name."</summary>
public sealed record AuditActor(ActorType Type, string Id);

public sealed record AuditTarget(string Type, string? Id);

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
