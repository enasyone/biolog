using Biolog.Features.TenantFeatureConfig.Audit;
using Biolog.Features.TenantFeatureConfig.Domain;
using Microsoft.AspNetCore.Http;

namespace Biolog.Features.TenantFeatureConfig.Api;

/// <summary>
/// No authentication story has landed in <c>api/Biolog.Api</c> yet. Every mutating endpoint
/// requires the caller to name themselves explicitly via <c>X-Actor-Id</c>, same disclosed
/// seam BIO-TEN-001's own <c>ActorResolver</c> established, copied here per this codebase's
/// "each feature keeps its own copy" convention.
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
            throw TenantFeatureConfigurationError.ActorRequired();
        }

        var actorTypeRaw = request.Headers[ActorTypeHeader].ToString();
        var actorType = ActorType.User;
        if (!string.IsNullOrEmpty(actorTypeRaw) && !ActorTypeExtensions.TryParse(actorTypeRaw, out actorType))
        {
            throw TenantFeatureConfigurationError.InvalidActorType(actorTypeRaw);
        }

        return new AuditActor(actorType, actorId);
    }
}
