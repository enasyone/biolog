using Biolog.Features.TransportEvents.Audit;
using Biolog.Features.TransportEvents.Domain;
using Microsoft.AspNetCore.Http;

namespace Biolog.Features.TransportEvents.Api;

/// <summary>
/// No authentication story has landed yet (BIO-INT-001, W12) - every endpoint requires the
/// caller to name themselves explicitly via <c>X-Actor-Id</c>, the same disclosed seam every
/// sibling story uses. ADR-0006 Decision 2: this feature has no authorization decision of its
/// own (see <c>TransportNotificationConfig</c>'s own header) - every use here is the
/// AUDIT-ATTRIBUTION use only, no provenance check.
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
            throw TransportEventError.ActorRequired();
        }

        var actorTypeRaw = request.Headers[ActorTypeHeader].ToString();
        var actorType = ActorType.User;
        if (!string.IsNullOrEmpty(actorTypeRaw) && !ActorTypeExtensions.TryParse(actorTypeRaw, out actorType))
        {
            throw TransportEventError.InvalidActorType(actorTypeRaw);
        }

        return new AuditActor(actorType, actorId);
    }
}
