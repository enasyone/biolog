using Biolog.Features.WorkforceAttendance.Audit;
using Biolog.Features.WorkforceAttendance.Domain;
using Microsoft.AspNetCore.Http;

namespace Biolog.Features.WorkforceAttendance.Api;

/// <summary>
/// No authentication story has landed yet - every endpoint requires the caller to name
/// themselves explicitly via <c>X-Actor-Id</c>, the same disclosed seam every sibling story
/// uses. ADR-0006 Decision 1: this is the asserted-actor accommodation's AUDIT-ATTRIBUTION use
/// for read endpoints (no provenance check required) and the AUTHORIZATION-DECISION use for
/// every write (shift management, adjustment creation), where <see cref="ActorProvenanceGate"/>
/// additionally verifies this exact resolved value before the service uses it.
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
            throw WorkforceAttendanceError.ActorRequired();
        }

        var actorTypeRaw = request.Headers[ActorTypeHeader].ToString();
        var actorType = ActorType.User;
        if (!string.IsNullOrEmpty(actorTypeRaw) && !ActorTypeExtensions.TryParse(actorTypeRaw, out actorType))
        {
            throw WorkforceAttendanceError.InvalidActorType(actorTypeRaw);
        }

        return new AuditActor(actorType, actorId);
    }
}
