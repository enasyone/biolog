namespace Biolog.Features.TransportNotifications;

/// <summary>Mirrors <c>ACTOR_TYPES</c> in <c>contracts/js/audit.mjs</c> (ADR-0003) - this
/// project's own copy, the same per-feature duplication every sibling story (BIO-ACC-001,
/// BIO-INT-003, BIO-INT-004) already established.</summary>
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
    public static bool TryParse(string? value, out ActorType type)
    {
        switch (value)
        {
            case "user": type = ActorType.User; return true;
            case "service_account": type = ActorType.ServiceAccount; return true;
            case "device": type = ActorType.Device; return true;
            case "system": type = ActorType.System; return true;
            case "anonymous": type = ActorType.Anonymous; return true;
            case "unknown": type = ActorType.Unknown; return true;
            default: type = default; return false;
        }
    }
}

/// <summary>ADR-0003: "structured, never a bare string: a name alone cannot tell a user from a service account of the same name."</summary>
public sealed record AuditActor(ActorType Type, string Id);
