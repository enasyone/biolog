using Microsoft.Extensions.Configuration;

namespace Biolog.Features.CardLifecycle.Domain;

/// <summary>
/// The real startup path for device credentials (TD-CRD-006-DENY-LIST-DEVICE-AUTH). Mirrors
/// BIO-PHO-002's own <c>DeviceCredentialProvisioning</c> exactly, under this feature's own
/// configuration section so the two features' device secrets are provisioned independently (the
/// same physical device presenting to two different feature surfaces is registered - and can be
/// rotated - separately under each). Reads every
/// <c>CardLifecycle:DeviceCredentials:&lt;deviceId&gt;</c> entry <see cref="IConfiguration"/>
/// exposes - ASP.NET Core's standard layered configuration, so an operator sets
/// <c>CardLifecycle__DeviceCredentials__&lt;deviceId&gt;=&lt;secret&gt;</c> as an environment variable
/// (or <c>dotnet user-secrets</c> in Development) with no code change. No secret value is
/// committed to this repository.
/// </summary>
public static class DeviceCredentialProvisioning
{
    public const string ConfigurationSectionPath = "CardLifecycle:DeviceCredentials";

    public static InMemoryDeviceCredentialStore LoadFromConfiguration(IConfiguration configuration)
    {
        var store = new InMemoryDeviceCredentialStore();
        foreach (var child in configuration.GetSection(ConfigurationSectionPath).GetChildren())
        {
            if (!string.IsNullOrEmpty(child.Value))
            {
                store.Register(child.Key, child.Value);
            }
        }

        return store;
    }
}
