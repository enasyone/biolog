using Biolog.Features.CardTemplates.Audit;
using Biolog.Features.CardTemplates.Domain;
using Microsoft.AspNetCore.Http;

namespace Biolog.Features.CardTemplates.Api;

/// <summary>No authentication story has landed in <c>api/Biolog.Api</c> yet (BIO-INT-001 is W12) - every endpoint requires the caller to name themselves explicitly via <c>X-Actor-Id</c>, the same disclosed seam every sibling story uses (ADR-0006 Decision 1). Here that identity IS the provenance record (who submitted/validated/approved/published/retired), not merely audit-attribution.</summary>
public static class ActorResolver
{
    public const string ActorIdHeader = "X-Actor-Id";
    public const string ActorTypeHeader = "X-Actor-Type";

    public static AuditActor Resolve(HttpRequest request)
    {
        var actorId = request.Headers[ActorIdHeader].ToString();
        if (string.IsNullOrWhiteSpace(actorId))
        {
            throw CardTemplateError.ActorRequired();
        }

        var actorTypeRaw = request.Headers[ActorTypeHeader].ToString();
        var actorType = ActorType.User;
        if (!string.IsNullOrEmpty(actorTypeRaw) && !ActorTypeExtensions.TryParse(actorTypeRaw, out actorType))
        {
            throw CardTemplateError.InvalidActorType(actorTypeRaw);
        }

        return new AuditActor(actorType, actorId);
    }
}
