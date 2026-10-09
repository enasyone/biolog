using Biolog.Features.PersonRegistry.Audit;
using Biolog.Features.PersonRegistry.Domain;
using Microsoft.AspNetCore.Http;

namespace Biolog.Features.PersonRegistry.Api;

/// <summary>
/// No authentication story has landed in <c>api/Biolog.Api</c> yet (BIO-INT-001, "auth
/// providers", is W12) - there is no principal to read a caller identity from. Rather than
/// fabricate one, every mutating person endpoint requires the caller to name themselves
/// explicitly via <c>X-Actor-Id</c>, so this story's audit trail records a real, asserted
/// identity instead of a placeholder - the same disclosed seam BIO-TEN-001/BIO-TEN-002
/// already use, pending BIO-INT-001.
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
            throw PersonError.ActorRequired();
        }

        var actorTypeRaw = request.Headers[ActorTypeHeader].ToString();
        var actorType = ActorType.User;
        if (!string.IsNullOrEmpty(actorTypeRaw) && !ActorTypeExtensions.TryParse(actorTypeRaw, out actorType))
        {
            throw PersonError.InvalidActorType(actorTypeRaw);
        }

        return new AuditActor(actorType, actorId);
    }
}
