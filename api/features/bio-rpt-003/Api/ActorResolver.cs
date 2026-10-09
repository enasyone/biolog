using Biolog.Features.AuditInvestigation.Audit;
using Biolog.Features.AuditInvestigation.Domain;
using Microsoft.AspNetCore.Http;

namespace Biolog.Features.AuditInvestigation.Api;

/// <summary>No authentication story has landed in <c>api/Biolog.Api</c> yet (BIO-INT-001 is W12) - every endpoint requires the caller to name themselves explicitly via <c>X-Actor-Id</c>, the same disclosed seam every sibling story uses (ADR-0006 Decision 1). Here that identity is ALSO what BIO-RPT-003-AC03/AC04's permission gates resolve against - who is asking decides what they see and whether they may export.</summary>
public static class ActorResolver
{
    public const string ActorIdHeader = "X-Actor-Id";
    public const string ActorTypeHeader = "X-Actor-Type";

    public static AuditActor Resolve(HttpRequest request)
    {
        var actorId = request.Headers[ActorIdHeader].ToString();
        if (string.IsNullOrWhiteSpace(actorId))
        {
            throw AuditInvestigationError.ActorRequired();
        }

        var actorTypeRaw = request.Headers[ActorTypeHeader].ToString();
        var actorType = ActorType.User;
        if (!string.IsNullOrEmpty(actorTypeRaw) && !ActorTypeExtensions.TryParse(actorTypeRaw, out actorType))
        {
            throw AuditInvestigationError.InvalidActorType(actorTypeRaw);
        }

        return new AuditActor(actorType, actorId);
    }
}
