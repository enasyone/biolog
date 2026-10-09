using Biolog.Features.Integrations.Audit;
using Biolog.Features.Integrations.Domain;
using Microsoft.AspNetCore.Http;

namespace Biolog.Features.Integrations.Api;

/// <summary>
/// No authentication story's token is wired into this feature (the same disclosed seam every
/// sibling story uses) - every endpoint requires the caller to name themselves explicitly via
/// <c>X-Actor-Id</c>. ADR-0006 Decision 1: this is the asserted-actor accommodation's
/// AUDIT-ATTRIBUTION use for preview/read endpoints (no provenance check required) and the
/// AUTHORIZATION-DECISION use for mapping-write/apply, where <see cref="ActorProvenanceGate"/>
/// additionally verifies this exact resolved value.
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
            throw ImportError.ActorRequired();
        }

        var actorTypeRaw = request.Headers[ActorTypeHeader].ToString();
        var actorType = ActorType.User;
        if (!string.IsNullOrEmpty(actorTypeRaw) && !ActorTypeExtensions.TryParse(actorTypeRaw, out actorType))
        {
            throw ImportError.InvalidActorType(actorTypeRaw);
        }

        return new AuditActor(actorType, actorId);
    }
}
