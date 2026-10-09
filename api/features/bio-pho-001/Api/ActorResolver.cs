using Biolog.Features.Photography.Audit;
using Biolog.Features.Photography.Domain;
using Microsoft.AspNetCore.Http;

namespace Biolog.Features.Photography.Api;

/// <summary>
/// No authentication story has landed yet (BIO-INT-001, W12) - every mutating endpoint here
/// requires the caller to name themselves explicitly via <c>X-Actor-Id</c>, the same disclosed
/// seam every sibling story in the identity/card wave already uses, pending BIO-INT-001.
///
/// <b>ADR-0006 Decision 1/2/4 applies here as the "audit-attribution" shape, not the
/// "authorization-decision" shape.</b> This feature has no <c>RoleType</c>, permission check,
/// or any other gate that makes what a caller may do depend on the asserted actor identity -
/// every endpoint is uniformly open to any caller with API access, exactly the same shape
/// ADR-0006 Decision 4 already catalogues for BIO-IDN-001 through BIO-IDN-005 (confirmed here
/// by direct inspection, not merely asserted: this project defines no authorization type at
/// all). A forged <c>X-Actor-Id</c> can misattribute who claims responsibility for a mutation
/// this feature already allowed any caller to perform - a disclosed non-repudiation residual
/// that closes only when BIO-INT-001 ships real identity, not a privilege-escalation gap. It
/// does not require an ActorProvenanceGate-style mitigation (BIO-IDN-006's own interim closure
/// for a genuine authorization-decision gap), and does not trigger a security review under the
/// same convention that correctly scoped one to BIO-IDN-006 alone.
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
            throw PhotoError.ActorRequired();
        }

        var actorTypeRaw = request.Headers[ActorTypeHeader].ToString();
        var actorType = ActorType.User;
        if (!string.IsNullOrEmpty(actorTypeRaw) && !ActorTypeExtensions.TryParse(actorTypeRaw, out actorType))
        {
            throw PhotoError.InvalidActorType(actorTypeRaw);
        }

        return new AuditActor(actorType, actorId);
    }
}
