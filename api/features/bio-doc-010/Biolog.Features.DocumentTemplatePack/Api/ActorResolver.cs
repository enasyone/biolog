using Microsoft.AspNetCore.Http;

namespace Biolog.Features.DocumentTemplatePack.Api;

/// <summary>
/// No authentication story has landed in <c>api/Biolog.Api</c> yet (BIO-INT-001 is W12) - the same
/// disclosed seam every prior BIO-DOC-00x/BIO-IDN/BIO-TEN story already uses. A caller may name
/// itself via <c>X-Actor-Id</c>.
///
/// <para>
/// <b>ADR-0006 Decision 1/2 compliance:</b> this is audit-attribution use only, never an
/// authorization decision - <see cref="DocumentTemplatePackEndpoints.IdentifyAsync"/> is open to
/// any caller regardless of which actor id is asserted (or none at all), and nothing here grants a
/// forged identity any capability a caller could not already exercise. This route also carries no
/// meaningful unauthorized-access risk beyond that: it returns only already-public, non-PII
/// governed reference-pack metadata (issuer, expected field names, security feature labels) and
/// cycles deterministically through three fixed, already-public SYNTHETIC demo scenarios - no
/// caller-supplied session id, no lookup of arbitrary tenant data (the same shape Zainab's
/// BIO-DOC-009-F01 review already approved for <c>AuthenticityEndpoints.ComputeAsync</c>).
/// </para>
/// </summary>
public static class ActorResolver
{
    public const string ActorIdHeader = "X-Actor-Id";
    public const string SyntheticUiActorId = "system:document-verify-ui";

    public static string ResolveOrDefault(HttpRequest request)
    {
        var actorId = request.Headers[ActorIdHeader].ToString();
        return string.IsNullOrWhiteSpace(actorId) ? SyntheticUiActorId : actorId;
    }
}
