using Biolog.Features.OperationalDashboard.Domain;
using Microsoft.AspNetCore.Http;

namespace Biolog.Features.OperationalDashboard.Api;

/// <summary>No authentication story has landed in <c>api/Biolog.Api</c> yet (BIO-INT-001 is W12) - every endpoint requires the caller to name themselves explicitly via <c>X-Actor-Id</c>, the same disclosed seam every sibling story uses. Here it resolves who <see cref="Domain.IDashboardPermissionGateway"/> checks (BIO-RPT-004-F01), not merely audit attribution.</summary>
public static class ActorIdHeader
{
    public const string HeaderName = "X-Actor-Id";

    public static string Resolve(HttpRequest request)
    {
        var actorId = request.Headers[HeaderName].ToString();
        if (string.IsNullOrWhiteSpace(actorId))
        {
            throw DashboardError.ActorRequired();
        }

        return actorId;
    }
}
