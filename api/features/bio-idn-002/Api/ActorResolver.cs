using Biolog.Features.PersonProfileRegistry.Audit;
using Biolog.Features.PersonProfileRegistry.Domain;
using Microsoft.AspNetCore.Http;

namespace Biolog.Features.PersonProfileRegistry.Api;

/// <summary>
/// No authentication story has landed yet (BIO-INT-001, W12) - every mutating profile endpoint
/// requires the caller to name themselves explicitly via <c>X-Actor-Id</c>, the same disclosed
/// seam BIO-TEN-001/BIO-TEN-002/BIO-IDN-001 already use, pending BIO-INT-001.
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
            throw PersonProfileError.ActorRequired();
        }

        var actorTypeRaw = request.Headers[ActorTypeHeader].ToString();
        var actorType = ActorType.User;
        if (!string.IsNullOrEmpty(actorTypeRaw) && !ActorTypeExtensions.TryParse(actorTypeRaw, out actorType))
        {
            throw PersonProfileError.InvalidActorType(actorTypeRaw);
        }

        return new AuditActor(actorType, actorId);
    }
}
