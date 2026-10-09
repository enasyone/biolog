using Biolog.Features.CardTemplates.Identifiers;
using Microsoft.AspNetCore.Http;

namespace Biolog.Features.CardTemplates.Api;

/// <summary>ADR-0004 Decision 3.2: a request may present <c>X-Correlation-Id</c>. If present and a well-formed canonical <c>evt_</c> identifier, it is used as-is; otherwise the server mints a fresh one.</summary>
public static class CorrelationIdHeader
{
    public const string HeaderName = "X-Correlation-Id";

    public static string Resolve(HttpRequest request)
    {
        var supplied = request.Headers[HeaderName].ToString();
        return CanonicalId.IsEventId(supplied) ? supplied : CanonicalId.NewEventId();
    }
}
