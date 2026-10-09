using Biolog.Features.TenantDomainRegistry.Audit;
using Biolog.Features.TenantDomainRegistry.Domain;
using Microsoft.AspNetCore.Http;

namespace Biolog.Features.TenantDomainRegistry.Api;

/// <summary>
/// No authentication story has landed in <c>api/Biolog.Api</c> yet (BIO-INT-001, "auth
/// providers", is W12) - there is no principal to read a caller identity from. Rather than
/// fabricate one, every mutating domain endpoint requires the caller to name themselves
/// explicitly via <c>X-Actor-Id</c>, so this story's audit trail records a real, asserted
/// identity instead of a placeholder. This header is a documented, disclosed seam pending
/// BIO-INT-001 - the same one BIO-TEN-001 already discloses - not a claim that this is how
/// the platform authenticates administrators in production.
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
            throw TenantDomainError.ActorRequired();
        }

        var actorTypeRaw = request.Headers[ActorTypeHeader].ToString();
        var actorType = ActorType.User;
        if (!string.IsNullOrEmpty(actorTypeRaw) && !ActorTypeExtensions.TryParse(actorTypeRaw, out actorType))
        {
            throw TenantDomainError.InvalidActorType(actorTypeRaw);
        }

        return new AuditActor(actorType, actorId);
    }
}
