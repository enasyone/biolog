using System.Security.Cryptography;
using System.Text;

namespace Biolog.Api.Features.BrandAssets;

/// <summary>
/// A per-tenant shared secret registry (BIO-CRD-001-QA-01 remediation). Nothing in this
/// module's HTTP surface can write to it - it is seeded out of band (local-dev
/// configuration or, in tests, direct DI resolution) - so an attacker cannot mint their own
/// tenant credential over the API the way they could freely declare a tenant ID before this
/// fix. This is what turns "which tenant a request claims" from a bare, forgeable assertion
/// into something the caller must also prove possession of.
/// </summary>
public interface ITenantCredentialStore
{
    /// <summary>Out-of-band provisioning only. Not reachable from any HTTP endpoint.</summary>
    void Register(string tenantId, string secret);

    /// <summary>Constant-time; a caller must not be able to time-probe whether a tenant id exists.</summary>
    bool Validate(string tenantId, string secret);
}

public sealed class InMemoryTenantCredentialStore : ITenantCredentialStore
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte[]> _secretHashes = new(StringComparer.Ordinal);

    // Zainab's finding during the TD-PROVENANCE-DUMMYKEY-SWEEP review (same bug class, a
    // different shape): the prior DummyHashFor(tenantId) derived the dummy from a known
    // public formula (SHA256 of a hardcoded literal plus the tenant id), so an attacker could
    // present that exact string as the "secret" for ANY unregistered tenant id and pass
    // Validate() - defeating BIO-CRD-001-QA-01's whole point. A single process-random value,
    // generated once and never tied to tenantId, closes that - still a constant-time-always-
    // runs comparison target (see Validate's own comment), just genuinely unguessable now
    // rather than a public constant.
    private static readonly byte[] DummyHash = RandomNumberGenerator.GetBytes(32);

    public void Register(string tenantId, string secret)
    {
        _secretHashes[tenantId] = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
    }

    public bool Validate(string tenantId, string secret)
    {
        // Always hash the presented secret, even for an unknown tenant, and always compare
        // in constant time - a store that short-circuits on "tenant not found" or uses a
        // length/early-exit comparison lets a caller distinguish "wrong secret" from
        // "unknown tenant" by timing, which is itself a tenant-enumeration oracle. Do NOT add
        // a ContainsKey(tenantId) condition here - that would reintroduce exactly that timing
        // oracle via && short-circuiting.
        var presented = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
        var expected = _secretHashes.TryGetValue(tenantId, out var hash) ? hash : DummyHash;
        return CryptographicOperations.FixedTimeEquals(presented, expected);
    }
}

/// <summary>
/// The real, production provisioning path for <see cref="ITenantCredentialStore"/>
/// (BIO-CRD-001-QA-02 remediation: the round-1 fix registered nothing outside tests, so
/// every real request 401'd). Reads <c>BrandAssets:TenantCredentials:&lt;tenantId&gt;</c>
/// from <see cref="IConfiguration"/> - ASP.NET Core's standard layered configuration
/// (appsettings, environment variables as
/// <c>BrandAssets__TenantCredentials__&lt;tenantId&gt;</c>, user-secrets in Development,
/// or a real secret manager in production). No secret value is ever committed to source:
/// nothing under this feature's checked-in <c>appsettings*.json</c> declares this section.
/// This is deliberately not an HTTP endpoint - provisioning is an operator/deployment
/// concern, not something a request to this API can trigger.
/// </summary>
public static class TenantCredentialProvisioning
{
    public const string ConfigurationSectionPath = "BrandAssets:TenantCredentials";

    public static ITenantCredentialStore LoadFromConfiguration(IConfiguration configuration)
    {
        var store = new InMemoryTenantCredentialStore();
        foreach (var entry in configuration.GetSection(ConfigurationSectionPath).GetChildren())
        {
            if (!string.IsNullOrWhiteSpace(entry.Value))
            {
                store.Register(entry.Key, entry.Value);
            }
        }
        return store;
    }
}
