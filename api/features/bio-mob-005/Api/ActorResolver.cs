using Biolog.Features.OfflineEnrolment.Audit;
using Biolog.Features.OfflineEnrolment.Domain;
using Microsoft.AspNetCore.Http;

namespace Biolog.Features.OfflineEnrolment.Api;

/// <summary>
/// No authentication story has landed yet (BIO-INT-001) - this feature's human-facing (browser
/// admin) capture-policy-write and duplicate-candidate-decision routes require the caller to
/// name themselves explicitly via <c>X-Actor-Id</c>, the same disclosed seam BIO-ACC-001/
/// BIO-ACC-003/BIO-IDN-006 already use. <see cref="ActorProvenanceGate"/> is checked immediately
/// after this resolves the value, before any authorization decision uses it (ADR-0006 Decision 3).
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
            throw EnrolmentError.ActorRequired();
        }

        var actorTypeRaw = request.Headers[ActorTypeHeader].ToString();
        var actorType = ActorType.User;
        if (!string.IsNullOrEmpty(actorTypeRaw) && !ActorTypeExtensions.TryParse(actorTypeRaw, out actorType))
        {
            throw EnrolmentError.InvalidActorType(actorTypeRaw);
        }

        return new AuditActor(actorType, actorId);
    }
}
