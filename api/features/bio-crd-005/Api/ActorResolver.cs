using Biolog.Features.CardIssuance.Audit;
using Biolog.Features.CardIssuance.Domain;
using Microsoft.AspNetCore.Http;

namespace Biolog.Features.CardIssuance.Api;

/// <summary>
/// No authentication story has landed in <c>api/Biolog.Api</c> yet (BIO-INT-001 is W12) -
/// there is no principal to read a caller identity from. Every mutating card-issue endpoint
/// requires the caller to name themselves explicitly via <c>X-Actor-Id</c>, so this story's
/// audit trail AND <see cref="Domain.CardIssueProvenance.Operator"/> record a real, asserted
/// identity instead of a placeholder - the same disclosed seam every sibling story uses,
/// pending BIO-INT-001. ADR-0006 Decision 1: audit-attribution use only - nothing about who
/// may issue/encode/print/verify/activate/deactivate a card is gated on this asserted identity.
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
            throw CardIssueError.ActorRequired();
        }

        var actorTypeRaw = request.Headers[ActorTypeHeader].ToString();
        var actorType = ActorType.User;
        if (!string.IsNullOrEmpty(actorTypeRaw) && !ActorTypeExtensions.TryParse(actorTypeRaw, out actorType))
        {
            throw CardIssueError.InvalidActorType(actorTypeRaw);
        }

        return new AuditActor(actorType, actorId);
    }
}
