using Biolog.Features.Visitors.Audit;
using Biolog.Features.Visitors.Domain;
using Microsoft.AspNetCore.Http;

namespace Biolog.Features.Visitors.Api;

/// <summary>
/// No authentication story has landed yet (BIO-INT-001, W12) - every endpoint requires the
/// caller to name themselves explicitly via <c>X-Actor-Id</c>, the same disclosed seam every
/// sibling story uses. ADR-0006 Decision 1: this is the asserted-actor accommodation's
/// AUDIT-ATTRIBUTION use for preregistration/read/check-in/check-out/evidence-capture endpoints
/// (no provenance check required) and the AUTHORIZATION-DECISION use for approve/deny and
/// config-management, where <see cref="ActorProvenanceGate"/> additionally verifies this exact
/// resolved value.
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
            throw VisitorError.ActorRequired();
        }

        var actorTypeRaw = request.Headers[ActorTypeHeader].ToString();
        var actorType = ActorType.User;
        if (!string.IsNullOrEmpty(actorTypeRaw) && !ActorTypeExtensions.TryParse(actorTypeRaw, out actorType))
        {
            throw VisitorError.InvalidActorType(actorTypeRaw);
        }

        return new AuditActor(actorType, actorId);
    }
}
