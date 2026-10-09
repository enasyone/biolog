using Biolog.Features.DocumentTemplateEditor.Audit;
using Biolog.Features.DocumentTemplateEditor.Domain;
using Microsoft.AspNetCore.Http;

namespace Biolog.Features.DocumentTemplateEditor.Api;

/// <summary>No authentication story has landed in <c>api/Biolog.Api</c> yet (BIO-INT-001 is W12) - every endpoint requires the caller to name themselves explicitly via <c>X-Actor-Id</c>, the same disclosed seam BIO-CRD-008 uses (ADR-0006 Decision 1). Here that identity IS the provenance record (who created/defined/submitted/published this draft), not merely audit-attribution - mirrored from BIO-CRD-008's own identical <c>ActorResolver</c> rather than BIO-DOC-010's own open, audit-attribution-only one, because every route here is a tenant-scoped mutation, not an open demo-cycling read.</summary>
public static class ActorResolver
{
    public const string ActorIdHeader = "X-Actor-Id";
    public const string ActorTypeHeader = "X-Actor-Type";

    public static AuditActor Resolve(HttpRequest request)
    {
        var actorId = request.Headers[ActorIdHeader].ToString();
        if (string.IsNullOrWhiteSpace(actorId))
        {
            throw DocumentTemplateDraftError.ActorRequired();
        }

        var actorTypeRaw = request.Headers[ActorTypeHeader].ToString();
        var actorType = ActorType.User;
        if (!string.IsNullOrEmpty(actorTypeRaw) && !ActorTypeExtensions.TryParse(actorTypeRaw, out actorType))
        {
            throw DocumentTemplateDraftError.InvalidActorType(actorTypeRaw);
        }

        return new AuditActor(actorType, actorId);
    }
}
