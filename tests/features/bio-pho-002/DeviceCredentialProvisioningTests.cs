using Biolog.Features.PhotoBooth.Domain;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Biolog.Features.PhotoBooth.Tests;

public sealed class DeviceCredentialProvisioningTests
{
    private static IConfiguration ConfigFrom(params (string key, string value)[] pairs)
    {
        var dict = pairs.ToDictionary(p => p.key, p => (string?)p.value);
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    [Fact]
    public void Loads_device_credentials_declared_under_the_configuration_section()
    {
        var configuration = ConfigFrom(
            ("PhotoBooth:DeviceCredentials:dev_018f2e5a-1234-7abc-89ab-0123456789ab", "s3cret-1"),
            ("PhotoBooth:DeviceCredentials:dev_018f2e5a-5678-7abc-89ab-0123456789ab", "s3cret-2"));

        var store = DeviceCredentialProvisioning.LoadFromConfiguration(configuration);

        Assert.True(store.Validate("dev_018f2e5a-1234-7abc-89ab-0123456789ab", "s3cret-1"));
        Assert.True(store.Validate("dev_018f2e5a-5678-7abc-89ab-0123456789ab", "s3cret-2"));
    }

    [Fact]
    public void Ignores_an_empty_configuration_section_rather_than_throwing()
    {
        var configuration = ConfigFrom(("SomeOtherSection:Key", "value"));

        var store = DeviceCredentialProvisioning.LoadFromConfiguration(configuration);

        Assert.False(store.Validate("anything", "anything"));
    }

    [Fact]
    public void This_mirrors_the_environment_variable_shape_an_operator_would_actually_set()
    {
        // ASP.NET Core's environment-variable configuration provider maps
        // PhotoBooth__DeviceCredentials__<deviceId>=<secret> to exactly this section shape.
        var configuration = ConfigFrom(("PhotoBooth:DeviceCredentials:dev_018f2e5a-9999-7abc-89ab-0123456789ab", "prod-secret"));

        var store = DeviceCredentialProvisioning.LoadFromConfiguration(configuration);

        Assert.True(store.Validate("dev_018f2e5a-9999-7abc-89ab-0123456789ab", "prod-secret"));
        Assert.False(store.Validate("dev_018f2e5a-9999-7abc-89ab-0123456789ab", "wrong"));
    }
}
