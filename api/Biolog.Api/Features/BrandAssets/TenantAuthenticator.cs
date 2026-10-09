using System.Text.RegularExpressions;

namespace Biolog.Api.Features.BrandAssets;

/// <summary>
/// Authenticates the tenant a brand-asset request acts as (BIO-CRD-001-AC05).
///
/// <b>Remediation for BIO-CRD-001-QA-01 (Critical, Seyi's independent review):</b> the
/// version of this module reviewed at commit <c>9372223</c> trusted a bare
/// <c>X-Biolog-Tenant-Id</c> header as tenant authority after only a syntax check. Any
/// caller could set that header to a victim tenant's id and the repository - which
/// correctly filters by whatever tenant string it is given - would filter by the
/// attacker's chosen victim. That is not tenant isolation; self-assertion with no
/// verification cannot ever satisfy "cross-tenant asset access is denied," by
/// construction, regardless of how correct the filtering logic downstream of it is.
///
/// This is still NOT an implementation of ADR-0002. Real tenant resolution
/// (host/principal/service channel agreement against a verified domain/binding registry)
/// is deliberately unbuilt anywhere in this host yet - <see cref="Tenancy.TenantContextMiddleware"/>
/// always leaves its accessor null, on purpose, pending BIO-TEN-001 - and building that
/// full mechanism is not in this story's <c>brand-assets</c> write scope. What changed is
/// narrower and closes the actual finding: a request must now also present a per-tenant
/// shared secret (<see cref="ITenantCredentialStore"/>), provisioned out of band and never
/// reachable through this module's own HTTP surface, so a caller can no longer simply
/// declare a tenant id and be granted access to it - it must also demonstrate possession
/// of that tenant's credential. Every failure mode (no id, no secret, malformed id, wrong
/// secret, someone else's valid secret paired with a different id) returns the identical
/// generic failure, so a caller cannot use this endpoint to probe which tenant ids exist
/// or which part of a forged pair was wrong.
///
/// When BIO-TEN-001 lands, only this class's implementation changes - callers depend on
/// <see cref="ITenantAuthenticator"/> alone.
/// </summary>
public interface ITenantAuthenticator
{
    /// <summary>Returns the authenticated tenant id, or null if authentication failed for any reason.</summary>
    string? TryAuthenticate(HttpRequest request);
}

public sealed partial class HeaderTenantAuthenticator(ITenantCredentialStore credentialStore) : ITenantAuthenticator
{
    public const string TenantIdHeaderName = "X-Biolog-Tenant-Id";
    public const string TenantSecretHeaderName = "X-Biolog-Tenant-Secret";

    // Real canonical tenant ids (ADR-0001 SS1.2, contracts/schemas/canonical-id.schema.json,
    // api/features/bio-ten-001/Identifiers/CanonicalId.cs) are `ten_<UUIDv7>` - an underscore
    // right after the prefix, not a bare slug. TD-CRD001-TENANT-ID-REGEX (Zainab's retroactive
    // security review): the prior slug-only pattern rejected every real id BIO-TEN-001's
    // registry can mint, so a genuine tenant could never authenticate here even with the
    // correct provisioned secret - TryAuthenticate returned null before the secret was ever
    // checked. This mirrors CanonicalId.cs's own TenantIdPattern exactly.
    [GeneratedRegex(@"^ten_[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$")]
    private static partial Regex TenantIdPattern();

    public string? TryAuthenticate(HttpRequest request)
    {
        var tenantId = SingleHeaderValue(request, TenantIdHeaderName);
        var secret = SingleHeaderValue(request, TenantSecretHeaderName);
        if (tenantId is null || secret is null || !TenantIdPattern().IsMatch(tenantId))
        {
            return null;
        }
        // Validate is called even when the id is well-formed but unregistered - the store's
        // own constant-time-with-a-dummy-hash behaviour is what keeps this branch and the
        // "known tenant, wrong secret" branch indistinguishable by timing.
        return credentialStore.Validate(tenantId, secret) ? tenantId : null;
    }

    private static string? SingleHeaderValue(HttpRequest request, string headerName)
    {
        if (!request.Headers.TryGetValue(headerName, out var values))
        {
            return null;
        }
        var value = values.ToString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
