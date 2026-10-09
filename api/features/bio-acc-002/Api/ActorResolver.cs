using Biolog.Features.AccessDecisions.Audit;
using Biolog.Features.AccessDecisions.Domain;
using Microsoft.AspNetCore.Http;

namespace Biolog.Features.AccessDecisions.Api;

/// <summary>
/// No authentication story has landed yet (BIO-INT-001, W12) - every endpoint requires the caller
/// to name themselves explicitly via <c>X-Actor-Id</c>, the same disclosed seam every sibling
/// story uses. Unlike a pure audit-attribution feature, THIS story's own decision endpoint reaches
/// a genuine authorization decision (see <see cref="ActorProvenanceGate"/>'s own header), so the
/// resolved actor here is also the one whose provenance and RBAC permission are checked downstream.
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
            throw AccessDecisionError.ActorRequired();
        }

        var actorTypeRaw = request.Headers[ActorTypeHeader].ToString();
        var actorType = ActorType.User;
        if (!string.IsNullOrEmpty(actorTypeRaw) && !ActorTypeExtensions.TryParse(actorTypeRaw, out actorType))
        {
            throw AccessDecisionError.InvalidActorType(actorTypeRaw);
        }

        return new AuditActor(actorType, actorId);
    }
}
