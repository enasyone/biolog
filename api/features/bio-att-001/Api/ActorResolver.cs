using Biolog.Features.Attendance.Audit;
using Biolog.Features.Attendance.Domain;
using Microsoft.AspNetCore.Http;

namespace Biolog.Features.Attendance.Api;

/// <summary>
/// No authentication story has landed yet (BIO-INT-001, W12) - every endpoint requires the
/// caller to name themselves explicitly via <c>X-Actor-Id</c>, the same disclosed seam every
/// sibling story uses, pending BIO-INT-001. ADR-0006 Decision 1: this is the asserted-actor
/// accommodation's AUDIT-ATTRIBUTION use for capture/read endpoints (no provenance check
/// required - see <see cref="AttendanceEventService"/>'s header) and the AUTHORIZATION-DECISION
/// use for the policy-management endpoint, where <see cref="ActorProvenanceGate"/> additionally
/// verifies this exact resolved value before <see cref="AttendancePolicyService"/> uses it.
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
            throw AttendanceError.ActorRequired();
        }

        var actorTypeRaw = request.Headers[ActorTypeHeader].ToString();
        var actorType = ActorType.User;
        if (!string.IsNullOrEmpty(actorTypeRaw) && !ActorTypeExtensions.TryParse(actorTypeRaw, out actorType))
        {
            throw AttendanceError.InvalidActorType(actorTypeRaw);
        }

        return new AuditActor(actorType, actorId);
    }
}
