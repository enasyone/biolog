using Biolog.Features.BiometricRetention.Domain;
using Microsoft.AspNetCore.Http;

namespace Biolog.Features.BiometricRetention.Api;

/// <summary>
/// No authentication story has landed yet (BIO-INT-001) - every mutating endpoint here requires
/// the caller to name themselves explicitly via <c>X-Actor-Id</c>, the same disclosed,
/// asserted-not-verified seam BIO-IDN-004's own <c>ActorResolver</c> already uses. A fresh,
/// project-local copy (this codebase's own "each feature project owns its small primitives"
/// convention), simplified to a plain actor id - this feature has no actor-type concept to carry.
/// </summary>
public static class ActorResolver
{
    public const string ActorIdHeader = "X-Actor-Id";

    public static string Resolve(HttpRequest request)
    {
        var actorId = request.Headers[ActorIdHeader].ToString();
        if (string.IsNullOrWhiteSpace(actorId))
        {
            throw BiometricRetentionException.ActorRequired();
        }

        return actorId;
    }
}
