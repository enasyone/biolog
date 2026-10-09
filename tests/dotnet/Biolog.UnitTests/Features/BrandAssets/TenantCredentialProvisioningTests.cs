// Developer tests for BIO-CRD-001-QA-02 (round-1's fix registered no credentials outside
// tests, so the composed production host could never authenticate a real request).
using Biolog.Api.Features.BrandAssets;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Biolog.UnitTests.Features.BrandAssets;

public sealed class TenantCredentialProvisioningTests
{
    private static IConfiguration ConfigFrom(params (string key, string value)[] pairs)
    {
        var dict = pairs.ToDictionary(p => p.key, p => (string?)p.value);
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    [Fact]
    public void Loads_tenant_credentials_declared_under_the_configuration_section()
    {
        var configuration = ConfigFrom(
            ("BrandAssets:TenantCredentials:alpha-state-university", "s3cret-1"),
            ("BrandAssets:TenantCredentials:beta-corp", "s3cret-2"));

        var store = TenantCredentialProvisioning.LoadFromConfiguration(configuration);

        Assert.True(store.Validate("alpha-state-university", "s3cret-1"));
        Assert.True(store.Validate("beta-corp", "s3cret-2"));
    }

    [Fact]
    public void Ignores_an_empty_configuration_section_rather_than_throwing()
    {
        var configuration = ConfigFrom(("SomeOtherSection:Key", "value"));

        var store = TenantCredentialProvisioning.LoadFromConfiguration(configuration);

        Assert.False(store.Validate("anything", "anything"));
    }

    [Fact]
    public void This_mirrors_the_environment_variable_shape_an_operator_would_actually_set()
    {
        // ASP.NET Core's environment-variable configuration provider maps
        // BrandAssets__TenantCredentials__<tenantId>=<secret> to exactly this section shape -
        // this test uses the in-memory provider only because it is deterministic in-process,
        // not because the real provisioning path is different in production.
        var configuration = ConfigFrom(("BrandAssets:TenantCredentials:prod-tenant", "prod-secret"));

        var store = TenantCredentialProvisioning.LoadFromConfiguration(configuration);

        Assert.True(store.Validate("prod-tenant", "prod-secret"));
        Assert.False(store.Validate("prod-tenant", "wrong"));
    }
}
