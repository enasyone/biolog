using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;

namespace Biolog.Api.Tenancy;

/// <summary>
/// TD-WEB-PROVENANCE-BRIDGE round 5 (ZB-WEBPROV-02) - the realization of Kemi's architectural
/// ruling after her round-4 Host-rewrite direction was found mechanically unworkable (Next.js's
/// own bundled rewrite proxy hardcodes <c>changeOrigin: true</c>, unconditionally clobbering any
/// Host header <c>proxy.ts</c> sets - confirmed by reading Next.js's own compiled source, not
/// assumed). Kemi verified that finding herself and reversed her own ruling rather than defend
/// it: a small, narrow backend addition instead of a hand-rolled reverse proxy in <c>proxy.ts</c>.
///
/// <b>What this closes.</b> Three rounds of fixes hardened HOW the web session token is signed,
/// gated, and revoked, but never restricted WHO may self-select WHICH role (ZB-WEBPROV-02) -
/// real web authentication is explicitly out of scope for this TD (Kemi's own round-2 ruling).
/// The token's own <c>tenantId</c> claim was also never backend-enforced: ADR-0002's only
/// implemented channel (<c>host</c>, <see cref="TenantContextMiddleware"/>) resolves the REAL
/// tenant from the HTTP <c>Host</c> header regardless of anything the token claims, so "confined
/// to synthetic data" was a doc-comment promise, not a backend guarantee.
///
/// <b>The fix, deliberately narrower than a general ADR-0002 Decision 1.1 <c>principal</c>
/// channel.</b> This does NOT resolve a per-caller tenant from the token's own claims by
/// trusting the claim string directly - it verifies the token's signature as a yes/no gate
/// ("is this a genuine, unexpired demo session, not a forgery"), then looks the VERIFIED
/// <c>tenantId</c> claim up in a small, server-configured, closed allowlist of pre-provisioned
/// synthetic tenant ids (see the OPS002-F01 note below), never constructing a
/// <see cref="TenantContext"/> from the claim string itself. A caller can still self-select
/// <c>admin</c> for demo purposes, but that identity can now never reach anywhere but one of
/// these quarantined tenants - the role-selection freedom round 1-4 could never safely
/// restrict is made harmless by confinement instead. Real per-caller tenant resolution from an
/// authenticated claim remains unbuilt; this is not a general <c>principal</c> channel
/// realization the way BIO-DEV-002's device-secret authentication (ADR-0002 Decision 7) is -
/// narrower on purpose, named "<c>demo-session</c>" below, not "<c>principal</c>", so a future
/// reader does not mistake this for that general capability.
///
/// <b>BIO-TEN-002 fix (OPS002-F01, High, 2026-10-07): the claim is now load-bearing.</b> Round
/// 5 shipped this resolver always returning the ONE configured
/// <c>WebSessionBridge:SyntheticTenantId</c> regardless of the verified claim's own
/// <c>tenantId</c> value - BIO-OPS-002's acceptance review found selecting alpha vs. beta in
/// the shell's tenant selector never actually changed the backend tenant, repository data, or
/// permission context, making AC01 cosmetic. <see cref="TryVerify"/> below now looks the
/// claim's <c>tenantId</c> ("alpha"/"beta" - the only two values <c>web/lib/demo-identity.ts</c>'s
/// <c>isDemoTenantId</c> lets the signer produce, so a validly-signed token's claim can never
/// be anything else) up in a two-entry map: "alpha" resolves to the original
/// <c>WebSessionBridge:SyntheticTenantId</c> (unchanged, so every existing demo-bridge story
/// that only configures this one key and only ever exercises the default tenant keeps working
/// identically), "beta" resolves to the new, OPTIONAL <c>WebSessionBridge:SyntheticTenantIdBeta</c>.
///
/// <b>Round 2 (SY-OPS002-03, High): fails CLOSED when beta is unconfigured, never falls back
/// to alpha's tenant.</b> A first pass here had "beta" silently resolve to the SAME tenant as
/// alpha when the second key was unset - Seyi's round-2 review correctly rejected that as the
/// identical cosmetic-switch defect OPS002-F01 was originally about, just moved one level down
/// (the DEFAULT/supported configuration has no beta key at all, so this fallback fired for
/// every real deployment today). <see cref="ResolveSyntheticTenantId"/> now returns <c>null</c>
/// in that case, and <see cref="TryVerify"/> rejects the whole token when it does - the SAME
/// "unconfigured means this mechanism never activates for this case" discipline
/// <see cref="LoadFromConfiguration"/> already applies to a missing/too-short secret, applied
/// one level down to a missing per-tenant mapping instead of substituting one. This stays a
/// closed, server-configured allowlist lookup, never the claim string used as an id directly -
/// the confinement property round 5 established is unchanged.
///
/// <b>Why this lives here, not a feature project.</b> <see cref="TenantContext"/>'s own
/// constructor is <c>internal</c> to this assembly (<c>Biolog.Api</c>) by design
/// (BIO-TEN-002-QA-03, Seyi round 3) - only code inside this assembly can construct one at all,
/// so this verifier cannot live in a feature project the way every <c>ActorProvenanceGate.cs</c>
/// copy does. Mirrors <see cref="Biolog.Api.Features.DeviceRegistry.ICrossTenantDeviceLookup"/>'s
/// own placement precedent (ADR-0002 Decision 7) for an identical reason.
///
/// <b>Why signature verification alone, no revocation check.</b> This file has no access to
/// the web side's own file-backed revocation store (`web/lib/session-revocation-store.ts`) -
/// a different process, a different stack, deliberately not a new cross-stack plumbing problem
/// this round takes on. It does not need one: <c>web/proxy.ts</c>'s own
/// <c>verifySessionToken</c> ALREADY performs the full check (signature, expiry, revocation,
/// the issuance-enabled flag) before ever forwarding <c>X-Web-Session-Token</c> at all - a
/// revoked or disabled-flag token is stripped web-side and never reaches this header. This
/// verifier's independent signature+expiry check exists so the BACKEND never has to trust
/// Next.js's own forwarding behavior as an uncertified boundary (the same "don't trust an
/// unverified claim from a proxy" principle Kemi's own X-Tenant-Id disclosure review raised) -
/// it is cheap, independent, defense-in-depth confirmation, not a replacement for the
/// cross-stack coordination problem revocation genuinely is.
///
/// <b>Secret and config, same fail-closed discipline as every secret in this codebase
/// (TD-PROVENANCE-DUMMYKEY-SWEEP).</b> <c>WebSessionBridge:TokenSecret</c> must be configured to
/// the EXACT SAME value as the web side's own <c>WEB_SESSION_TOKEN_SECRET</c> - a shared secret
/// by construction, the same cross-stack-shared shape every <c>*ActorProvenanceSecret</c> key
/// already has. <c>WebSessionBridge:SyntheticTenantId</c> is the hardcoded tenant id "alpha"
/// confines to; the OPTIONAL <c>WebSessionBridge:SyntheticTenantIdBeta</c> (OPS002-F01 fix) is
/// what "beta" confines to - left unset, a "beta" claim is rejected outright (SY-OPS002-03,
/// round 2: never a silent fallback to alpha's tenant) - any syntactically well-formed
/// <c>ten_</c> id works for either (confirmed: nothing downstream
/// requires a tenant id to be pre-registered anywhere to function as an ordinary,
/// currently-ungranted tenant). The secret or the primary tenant id unconfigured (or the secret
/// too short) means this resolver never activates at all - every request falls through to the
/// existing host-channel path exactly as before this file existed, never a forged or default
/// demo tenant.
/// </summary>
public sealed class DemoSessionTenantResolver
{
    public const int MinimumSecretLength = 32;

    private readonly byte[]? _key;
    private readonly string? _syntheticTenantId;
    private readonly string? _syntheticTenantIdBeta;

    private DemoSessionTenantResolver(byte[]? key, string? syntheticTenantId, string? syntheticTenantIdBeta)
    {
        _key = key;
        _syntheticTenantId = syntheticTenantId;
        _syntheticTenantIdBeta = syntheticTenantIdBeta;
    }

    public static DemoSessionTenantResolver LoadFromConfiguration(IConfiguration configuration)
    {
        var secret = configuration["WebSessionBridge:TokenSecret"];
        var tenantId = configuration["WebSessionBridge:SyntheticTenantId"];
        var tenantIdBeta = configuration["WebSessionBridge:SyntheticTenantIdBeta"];

        var key = !string.IsNullOrEmpty(secret) && secret.Length >= MinimumSecretLength
            ? Encoding.UTF8.GetBytes(secret)
            : null;

        return new DemoSessionTenantResolver(
            key,
            string.IsNullOrWhiteSpace(tenantId) ? null : tenantId,
            string.IsNullOrWhiteSpace(tenantIdBeta) ? null : tenantIdBeta);
    }

    /// <summary>
    /// Returns a resolved <see cref="TenantContext"/> confined to whichever configured
    /// synthetic tenant the verified claim's own <c>tenantId</c> maps to (OPS002-F01 fix - see
    /// <see cref="TryVerify"/>) if <paramref name="presentedToken"/> is a genuinely signed,
    /// unexpired demo session token - <c>null</c> otherwise (malformed, bad signature, expired,
    /// or this resolver itself unconfigured), so <see cref="TenantContextMiddleware"/> falls
    /// through to its existing host-channel resolution exactly as it did before this resolver
    /// existed.
    /// </summary>
    public TenantContext? TryResolve(string? presentedToken) => TryVerify(presentedToken)?.Context;

    /// <summary>
    /// The full verified shape <see cref="TryResolve"/> only returns the tenant half of -
    /// <see cref="DemoSessionContextAuditEndpoints"/> (OPS002-F02 fix) needs the verified
    /// <c>roleId</c> and the raw <c>tenantId</c> claim too, to attribute and describe an audit
    /// event, and must not re-implement this same HMAC verification a second time to get them.
    /// </summary>
    public readonly record struct DemoSessionVerification(TenantContext Context, string RoleId, string DemoTenantId);

    public DemoSessionVerification? TryVerify(string? presentedToken)
    {
        if (_key is null || _syntheticTenantId is null || string.IsNullOrEmpty(presentedToken))
        {
            return null;
        }

        var separatorIndex = presentedToken.IndexOf('.');
        if (separatorIndex <= 0 || separatorIndex == presentedToken.Length - 1)
        {
            return null;
        }

        var payloadB64 = presentedToken[..separatorIndex];
        var presentedSignatureHex = presentedToken[(separatorIndex + 1)..];

        var expected = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(payloadB64));

        byte[] presented;
        try
        {
            presented = Convert.FromHexString(presentedSignatureHex);
        }
        catch (FormatException)
        {
            return null;
        }

        if (!CryptographicOperations.FixedTimeEquals(presented, expected))
        {
            return null;
        }

        DemoSessionTokenClaims claims;
        try
        {
            var json = DecodeBase64Url(payloadB64);
            var parsed = JsonSerializer.Deserialize<DemoSessionTokenClaims>(json);
            if (parsed is null ||
                string.IsNullOrEmpty(parsed.Sid) ||
                string.IsNullOrEmpty(parsed.RoleId) ||
                string.IsNullOrEmpty(parsed.TenantId) ||
                parsed.IssuedAtMs is null or <= 0 ||
                parsed.ExpiresAtMs is null or <= 0)
            {
                return null;
            }
            claims = parsed;
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return null;
        }

        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (nowMs > claims.ExpiresAtMs)
        {
            return null;
        }

        var resolvedTenantId = ResolveSyntheticTenantId(claims.TenantId!);
        if (resolvedTenantId is null)
        {
            // SY-OPS002-03 (High, round 2): fail closed, not a silent fallback - see this
            // method's own header.
            return null;
        }

        var context = new TenantContext(resolvedTenantId, bindingKey: string.Empty, channels: new[] { "demo-session" });
        return new DemoSessionVerification(context, claims.RoleId!, claims.TenantId!);
    }

    /// <summary>
    /// OPS002-F01 fix, round 2 (SY-OPS002-03, High): looks the verified claim's own
    /// <c>tenantId</c> up in the two-entry allowlist instead of always returning
    /// <c>_syntheticTenantId</c> regardless of it, and returns <c>null</c> - never falling back
    /// to alpha's tenant - when the claimed tenant's own config key is not set.
    ///
    /// Round 1 of this fix had "beta" silently fall back to alpha's tenant when
    /// <c>WebSessionBridge:SyntheticTenantIdBeta</c> was unconfigured. Seyi's round-2 review
    /// correctly rejected that: in the DEFAULT/supported configuration (nobody sets the beta
    /// key today), selecting "beta" still silently resolved to alpha's tenant - the exact
    /// cosmetic-switch defect the ORIGINAL OPS002-F01 finding was about, just moved one level
    /// down and partly hidden by round 1's own test asserting that fallback as the intended
    /// behavior rather than testing against it. Fixed by following THIS SAME CLASS's own
    /// already-established precedent for "optional config gap" (see <see cref="LoadFromConfiguration"/>'s
    /// own secret-too-short handling): unconfigured means the mechanism never activates for
    /// that specific case, falling through to <see cref="TenantContextMiddleware"/>'s ordinary
    /// host-channel resolution (ordinarily <c>tenant_context_missing</c> in a dev/test
    /// environment with no real registered host) - never a substituted tenant. Any value other
    /// than "alpha"/"beta" also returns <c>null</c>; unreachable in practice, since
    /// <c>web/lib/demo-identity.ts</c>'s <c>isDemoTenantId</c> is the only path that can ever
    /// produce a validly-signed claim, and it accepts only those two values.
    /// </summary>
    private string? ResolveSyntheticTenantId(string demoTenantId) =>
        demoTenantId switch
        {
            "alpha" => _syntheticTenantId,
            "beta" => _syntheticTenantIdBeta,
            _ => null,
        };

    private static string DecodeBase64Url(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        var padding = (4 - base64.Length % 4) % 4;
        base64 = base64.PadRight(base64.Length + padding, '=');
        return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
    }

    private sealed class DemoSessionTokenClaims
    {
        [JsonPropertyName("sid")]
        public string? Sid { get; init; }

        [JsonPropertyName("roleId")]
        public string? RoleId { get; init; }

        [JsonPropertyName("tenantId")]
        public string? TenantId { get; init; }

        [JsonPropertyName("issuedAtMs")]
        public double? IssuedAtMs { get; init; }

        [JsonPropertyName("expiresAtMs")]
        public double? ExpiresAtMs { get; init; }
    }
}
