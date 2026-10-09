using Biolog.Features.AccessPolicy.Audit;
using Biolog.Features.AccessPolicy.Domain;
using Microsoft.AspNetCore.Http;

namespace Biolog.Features.AccessPolicy.Api;

/// <summary>
/// No authentication story has landed yet (BIO-INT-001, W12) - every mutating and evaluating
/// endpoint here requires the caller to name themselves explicitly via <c>X-Actor-Id</c>, the same
/// disclosed seam every sibling story uses, pending BIO-INT-001.
///
/// <b>ADR-0006 Decision 3, applied from the start.</b> <c>X-Actor-Id</c> is a caller-supplied
/// ASSERTION with no authenticated principal binding - a caller who asserts a KNOWN or GUESSED
/// privileged person id is authorized as that person, since authorization can only ever be as
/// strong as the identity it trusts. <see cref="ActorProvenanceGate"/> is this story's own
/// application-side half of Decision 3's interim mitigation (checked immediately AFTER this
/// method resolves the value, before any authorization decision uses it, on every mutating and
/// evaluating endpoint) - see that class's own header for the full ADR-0006 rationale and
/// disclosed residual scope; docs/adr/ADR-0006-actor-identity-and-authorization-boundary.md is the
/// authoritative text this class implements against.
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
            throw AccessPolicyError.ActorRequired();
        }

        var actorTypeRaw = request.Headers[ActorTypeHeader].ToString();
        var actorType = ActorType.User;
        if (!string.IsNullOrEmpty(actorTypeRaw) && !ActorTypeExtensions.TryParse(actorTypeRaw, out actorType))
        {
            throw AccessPolicyError.InvalidActorType(actorTypeRaw);
        }

        return new AuditActor(actorType, actorId);
    }
}
