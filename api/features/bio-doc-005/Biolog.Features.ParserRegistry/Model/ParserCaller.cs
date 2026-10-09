namespace Biolog.Features.ParserRegistry.Model;

/// <summary>
/// Who is calling this domain library - BTS (the platform operator) or one specific tenant
/// (BIO-DOC-005-AC01/AC05). Deliberately a plain value this API's caller supplies, exactly like
/// <c>DocumentSession.Open</c>'s plain <c>tenantId</c> string (BIO-DOC-001): this is NOT an
/// authentication result, and this library does not resolve it from any header or token. ADR-0006
/// ("Actor identity and the authorization boundary pending real authentication") is explicit that
/// the asserted-actor accommodation is unsafe as the sole input to an authorization decision - and
/// this story's AC01/AC05 genuinely are authorization decisions (who may author a parser; whether
/// a tenant may see another tenant's). Rather than resolve identity unsafely from a forgeable
/// header the way BIO-IDN-006 round 1 did, this library takes no HTTP surface at all (the same
/// "not wired into Biolog.Api" scope every BIO-DOC-00x story before this one has kept) and simply
/// enforces its rules against whatever <see cref="ParserCaller"/> its own caller provides. Wiring
/// a real, provenance-verified actor into this parameter is a later story's job, once BIO-INT-001
/// or an equivalent verified-identity mechanism exists.
/// </summary>
public abstract record ParserCaller
{
    private ParserCaller()
    {
    }

    public sealed record Bts : ParserCaller;

    public sealed record Tenant : ParserCaller
    {
        public string TenantId { get; }

        public Tenant(string tenantId)
        {
            if (string.IsNullOrWhiteSpace(tenantId))
            {
                throw new ArgumentException("must be a non-empty string", nameof(tenantId));
            }

            TenantId = tenantId;
        }
    }
}
