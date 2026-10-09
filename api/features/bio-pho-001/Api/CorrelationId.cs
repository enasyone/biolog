using Biolog.Features.Photography.Identifiers;
using Microsoft.AspNetCore.Http;

namespace Biolog.Features.Photography.Api;

/// <summary>ADR-0004 Decision 3.2: a request may present <c>X-Correlation-Id</c>. If present and well-formed, used as-is; otherwise a fresh one is minted.</summary>
public static class CorrelationIdHeader
{
    public const string HeaderName = "X-Correlation-Id";

    public static string Resolve(HttpRequest request)
    {
        var supplied = request.Headers[HeaderName].ToString();
        return CanonicalId.IsEventId(supplied) ? supplied : CanonicalId.NewEventId();
    }
}
