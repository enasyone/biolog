using Biolog.Features.TenantRegistry.Audit;
using Biolog.Features.TenantRegistry.Domain;
using Microsoft.AspNetCore.Http;

namespace Biolog.Features.TenantRegistry.Api;

/// <summary>
/// No authentication story has landed in <c>api/Biolog.Api</c> yet (BIO-INT-001, "auth
/// providers", is W12) - there is no principal to read a caller identity from. Rather than
/// fabricate one (the same "no implicit default" posture ADR-0002 §2 takes toward tenancy),
/// every mutating tenant endpoint requires the caller to name themselves explicitly via
/// <c>X-Actor-Id</c>, so BIO-TEN-001-AC04's audit trail records a real, asserted identity
/// instead of a placeholder. This header is a documented, disclosed seam pending BIO-INT-001;
/// it is not a claim that this is how the platform authenticates administrators in production.
/// </summary>
public static class ActorResolver
{
    public const string ActorIdHeader = "X-Actor-Id";
    public const string ActorTypeHeader = "X-Actor-Type";

    public static AuditActor Resolve(HttpRequest request)
    {
        var actorId = request.Headers[ActorIdHeader].ToString();
        if (string.IsNullOrWhiteSpace(actorId))
        {
            throw TenantRegistryError.ActorRequired();
        }

        var actorTypeRaw = request.Headers[ActorTypeHeader].ToString();
        var actorType = ActorType.User;
        if (!string.IsNullOrEmpty(actorTypeRaw) && !ActorTypeExtensions.TryParse(actorTypeRaw, out actorType))
        {
            throw TenantRegistryError.InvalidActorType(actorTypeRaw);
        }

        return new AuditActor(actorType, actorId);
    }
}
