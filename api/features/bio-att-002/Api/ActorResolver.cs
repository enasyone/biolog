using Biolog.Features.AttendanceSessions.Audit;
using Biolog.Features.AttendanceSessions.Domain;
using Microsoft.AspNetCore.Http;

namespace Biolog.Features.AttendanceSessions.Api;

/// <summary>
/// No authentication story has landed yet (BIO-INT-001, W12) - every endpoint requires the caller
/// to name themselves explicitly via <c>X-Actor-Id</c>, the same disclosed seam every sibling story
/// uses. ADR-0006 Decision 1: this is the asserted-actor accommodation's AUDIT-ATTRIBUTION use for
/// session/roster CRUD and the read-side roster view (no provenance check required), and the
/// AUTHORIZATION-DECISION use for the policy-management and adjustment endpoints, where
/// <see cref="ActorProvenanceGate"/> additionally verifies this exact resolved value before
/// <see cref="SessionAttendancePolicyService"/>/<see cref="AttendanceAdjustmentService"/> use it.
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
            throw SessionError.ActorRequired();
        }

        var actorTypeRaw = request.Headers[ActorTypeHeader].ToString();
        var actorType = ActorType.User;
        if (!string.IsNullOrEmpty(actorTypeRaw) && !ActorTypeExtensions.TryParse(actorTypeRaw, out actorType))
        {
            throw SessionError.InvalidActorType(actorTypeRaw);
        }

        return new AuditActor(actorType, actorId);
    }
}
