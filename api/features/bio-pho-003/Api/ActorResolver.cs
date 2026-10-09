using Biolog.Features.PhotoQuality.Audit;
using Biolog.Features.PhotoQuality.Domain;
using Microsoft.AspNetCore.Http;

namespace Biolog.Features.PhotoQuality.Api;

/// <summary>
/// Resolves the caller's asserted actor id/type from the request headers. This is ONLY the
/// identity assertion step - on its own it proves nothing (ADR-0006 Decision 1/4's bare
/// "audit-attribution" shape).
///
/// <b>ZB-PHO003-01 (Zainab, security round 1, Critical), fixed.</b> Round 1's own header here
/// claimed this feature "defines no RoleType, permission check, or any other gate" and so
/// crosses "no privilege-escalation boundary" - that was wrong by the time it was written:
/// approving a capture calls <c>PhotoMasterService.AppendAsync</c>, replacing a person's real
/// authoritative photo, and this build already contains the canonical provenance+RBAC
/// infrastructure every current tenant-plane story uses. Every route this resolver feeds now
/// requires the resolved actor id to also pass <see cref="ActorProvenanceGate.Require"/> (proof
/// the caller genuinely is who they assert) and a tenant-scoped
/// <see cref="IPermissionGateway.ActorHasPermissionAsync"/> grant for that route's own
/// <see cref="PhotoQualityPermissions"/> key - see <c>PhotoQualityEndpoints.AuthorizeAsync</c>.
/// This resolver itself still only ever parses the header; it is the two checks layered on top
/// of its result, not this class, that make the asserted identity trustworthy.
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
            throw PhotoQualityError.ActorRequired();
        }

        var actorTypeRaw = request.Headers[ActorTypeHeader].ToString();
        var actorType = ActorType.User;
        if (!string.IsNullOrEmpty(actorTypeRaw) && !ActorTypeExtensions.TryParse(actorTypeRaw, out actorType))
        {
            throw PhotoQualityError.InvalidActorType(actorTypeRaw);
        }

        return new AuditActor(actorType, actorId);
    }
}
