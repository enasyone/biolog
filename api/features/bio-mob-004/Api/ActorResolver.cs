using Biolog.Features.MobileVerification.Audit;
using Biolog.Features.MobileVerification.Domain;
using Microsoft.AspNetCore.Http;

namespace Biolog.Features.MobileVerification.Api;

/// <summary>
/// No authentication story has landed yet (BIO-INT-001) - this feature's one human-facing
/// (tenant-admin) route, assigning a device's verification role (AC03), requires the caller to
/// name themselves explicitly via <c>X-Actor-Id</c>, the same disclosed seam BIO-ACC-001/
/// BIO-ACC-003/BIO-IDN-006/BIO-MOB-005 already use. <see cref="ActorProvenanceGate"/> is checked
/// immediately after this resolves the value, before any authorization decision uses it
/// (ADR-0006 Decision 3).
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
            throw VerificationError.ActorRequired();
        }

        var actorTypeRaw = request.Headers[ActorTypeHeader].ToString();
        var actorType = ActorType.User;
        if (!string.IsNullOrEmpty(actorTypeRaw) && !ActorTypeExtensions.TryParse(actorTypeRaw, out actorType))
        {
            throw VerificationError.InvalidActorType(actorTypeRaw);
        }

        return new AuditActor(actorType, actorId);
    }
}
