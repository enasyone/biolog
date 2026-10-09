using Biolog.Features.AccessCache.Audit;
using Biolog.Features.AccessCache.Domain;
using Microsoft.AspNetCore.Http;

namespace Biolog.Features.AccessCache.Api;

/// <summary>
/// No authentication story has landed yet (BIO-INT-001) - this feature's one human-facing
/// (browser <c>AccessService</c>) route requires the caller to name themselves explicitly via
/// <c>X-Actor-Id</c>, the same disclosed seam BIO-ACC-001/BIO-IDN-006 already use.
/// <see cref="ActorProvenanceGate"/> is checked immediately after this resolves the value,
/// before any authorization decision uses it (ADR-0006 Decision 3).
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
            throw AccessCacheError.ActorRequired();
        }

        var actorTypeRaw = request.Headers[ActorTypeHeader].ToString();
        var actorType = ActorType.User;
        if (!string.IsNullOrEmpty(actorTypeRaw) && !ActorTypeExtensions.TryParse(actorTypeRaw, out actorType))
        {
            throw AccessCacheError.InvalidActorType(actorTypeRaw);
        }

        return new AuditActor(actorType, actorId);
    }
}
