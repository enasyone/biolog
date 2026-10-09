using Biolog.Features.PersonLifecycle.Audit;
using Biolog.Features.PersonLifecycle.Domain;
using Microsoft.AspNetCore.Http;

namespace Biolog.Features.PersonLifecycle.Api;

/// <summary>
/// No authentication story has landed yet (BIO-INT-001, W12) - every mutating endpoint here
/// requires the caller to name themselves explicitly via <c>X-Actor-Id</c>, the same disclosed
/// seam every prior identity-wave story already uses, pending BIO-INT-001.
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
            throw PersonLifecycleError.ActorRequired();
        }

        var actorTypeRaw = request.Headers[ActorTypeHeader].ToString();
        var actorType = ActorType.User;
        if (!string.IsNullOrEmpty(actorTypeRaw) && !ActorTypeExtensions.TryParse(actorTypeRaw, out actorType))
        {
            throw PersonLifecycleError.InvalidActorType(actorTypeRaw);
        }

        return new AuditActor(actorType, actorId);
    }
}
