// Developer tests for BIO-CRD-001-AC05 and its BIO-CRD-001-QA-01 remediation (Seyi's
// independent review: a bare, unverified tenant-id header grants access to any tenant).
using Biolog.Api.Features.BrandAssets;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Biolog.UnitTests.Features.BrandAssets;

public sealed class HeaderTenantAuthenticatorTests
{
    // A genuine ADR-0001 SS1.2 canonical tenant id (`ten_<UUIDv7>`), the exact shape
    // api/features/bio-ten-001/Identifiers/CanonicalId.cs mints. TD-CRD001-TENANT-ID-REGEX
    // (Zainab's retroactive security review): every fixture below used to be a bare slug
    // like "tenant-a", which the old, too-narrow TenantIdPattern happened to accept - so no
    // test here ever exercised a real canonical id, and the regex silently rejected every
    // genuine tenant BIO-TEN-001 can actually mint.
    private static string NewTenantId() => $"ten_{Guid.CreateVersion7():D}";

    private static HttpRequest RequestWith(string? tenantId, string? secret)
    {
        var context = new DefaultHttpContext();
        if (tenantId is not null) context.Request.Headers[HeaderTenantAuthenticator.TenantIdHeaderName] = tenantId;
        if (secret is not null) context.Request.Headers[HeaderTenantAuthenticator.TenantSecretHeaderName] = secret;
        return context.Request;
    }

    [Fact]
    public void Authenticates_a_tenant_that_presents_its_own_correct_secret()
    {
        var tenantId = NewTenantId();
        var store = new InMemoryTenantCredentialStore();
        store.Register(tenantId, "correct-secret");
        var authenticator = new HeaderTenantAuthenticator(store);

        var result = authenticator.TryAuthenticate(RequestWith(tenantId, "correct-secret"));

        Assert.Equal(tenantId, result);
    }

    [Fact]
    public void Rejects_a_request_with_no_headers_at_all()
    {
        var authenticator = new HeaderTenantAuthenticator(new InMemoryTenantCredentialStore());
        Assert.Null(authenticator.TryAuthenticate(RequestWith(null, null)));
    }

    [Fact]
    public void Rejects_a_well_formed_tenant_id_with_no_secret()
    {
        var tenantId = NewTenantId();
        var store = new InMemoryTenantCredentialStore();
        store.Register(tenantId, "secret-a");
        var authenticator = new HeaderTenantAuthenticator(store);

        Assert.Null(authenticator.TryAuthenticate(RequestWith(tenantId, null)));
    }

    [Fact]
    public void Rejects_a_registered_tenant_id_with_the_wrong_secret()
    {
        var tenantId = NewTenantId();
        var store = new InMemoryTenantCredentialStore();
        store.Register(tenantId, "secret-a");
        var authenticator = new HeaderTenantAuthenticator(store);

        Assert.Null(authenticator.TryAuthenticate(RequestWith(tenantId, "not-secret-a")));
    }

    [Fact]
    public void Rejects_an_unregistered_tenant_id_regardless_of_the_secret_presented()
    {
        var authenticator = new HeaderTenantAuthenticator(new InMemoryTenantCredentialStore());
        Assert.Null(authenticator.TryAuthenticate(RequestWith(NewTenantId(), "anything")));
    }

    [Fact]
    public void QA01_a_caller_cannot_claim_a_victim_tenant_by_pairing_its_id_with_its_own_valid_secret()
    {
        // This is the exact BIO-CRD-001-QA-01 attack: the caller genuinely owns the
        // attacker tenant and its real secret, and simply swaps the id header to the
        // victim's. Before the fix this was indistinguishable from a legitimate request
        // because the id alone was treated as authority.
        var victimTenantId = NewTenantId();
        var attackerTenantId = NewTenantId();
        var store = new InMemoryTenantCredentialStore();
        store.Register(victimTenantId, "victim-secret");
        store.Register(attackerTenantId, "attacker-secret");
        var authenticator = new HeaderTenantAuthenticator(store);

        var forged = authenticator.TryAuthenticate(RequestWith(victimTenantId, "attacker-secret"));

        Assert.Null(forged);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Has Spaces")]
    [InlineData("UPPERCASE")]
    [InlineData("-leading-hyphen")]
    [InlineData("tenant-a")] // a bare slug - what the old, too-narrow pattern wrongly accepted
    [InlineData("ten-018f2e5a-1234-7abc-89ab-0123456789ab")] // hyphen, not underscore, after the prefix
    [InlineData("per_018f2e5a-1234-7abc-89ab-0123456789ab")] // a different type's prefix (ADR-0001)
    [InlineData("ten_018f2e5a-1234-6abc-89ab-0123456789ab")] // UUID version nibble is not 7
    [InlineData("ten_018F2E5A-1234-7ABC-89AB-0123456789AB")] // canonical ids are lowercase only
    public void Rejects_a_malformed_tenant_id_even_with_a_correct_looking_secret(string tenantId)
    {
        var authenticator = new HeaderTenantAuthenticator(new InMemoryTenantCredentialStore());
        Assert.Null(authenticator.TryAuthenticate(RequestWith(tenantId, "anything")));
    }

    [Fact]
    public void Authenticates_a_real_canonical_ten_prefixed_uuid_v7_tenant_id()
    {
        // TD-CRD001-TENANT-ID-REGEX regression test: a genuine id in the exact shape
        // CanonicalId.NewTenantId() mints must authenticate when its secret is correct -
        // the defect this closes was that TenantIdPattern rejected every id in this shape
        // before the secret was ever checked.
        const string tenantId = "ten_018f2e5a-1234-7abc-89ab-0123456789ab";
        var store = new InMemoryTenantCredentialStore();
        store.Register(tenantId, "correct-secret");
        var authenticator = new HeaderTenantAuthenticator(store);

        Assert.Equal(tenantId, authenticator.TryAuthenticate(RequestWith(tenantId, "correct-secret")));
    }
}

public sealed class InMemoryTenantCredentialStoreTests
{
    [Fact]
    public void Validate_is_true_only_for_the_exact_registered_secret()
    {
        var store = new InMemoryTenantCredentialStore();
        store.Register("ten-a", "s3cret");

        Assert.True(store.Validate("ten-a", "s3cret"));
        Assert.False(store.Validate("ten-a", "wrong"));
        Assert.False(store.Validate("ten-b", "s3cret"));
    }

    [Fact]
    public void Register_can_rotate_a_tenants_secret()
    {
        var store = new InMemoryTenantCredentialStore();
        store.Register("ten-a", "old-secret");
        store.Register("ten-a", "new-secret");

        Assert.False(store.Validate("ten-a", "old-secret"));
        Assert.True(store.Validate("ten-a", "new-secret"));
    }

    // Zainab's finding (TD-PROVENANCE-DUMMYKEY-SWEEP security review): the prior
    // DummyHashFor(tenantId) = SHA256("\0no-such-tenant\0" + tenantId) was a known public
    // formula, so this exact "secret" forged a pass for ANY unregistered tenant id -
    // defeating BIO-CRD-001-QA-01's whole point. Regression: this former dummy preimage must
    // now fail against a genuine, never-registered canonical tenant id.
    [Fact]
    public void Validate_rejects_the_former_public_dummy_preimage_for_an_unregistered_tenant()
    {
        var store = new InMemoryTenantCredentialStore();
        var unregisteredTenantId = $"ten_{Guid.CreateVersion7():D}";
        var formerDummyPreimage = $"\0no-such-tenant\0{unregisteredTenantId}";

        Assert.False(store.Validate(unregisteredTenantId, formerDummyPreimage));
    }
}
