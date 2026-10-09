using Microsoft.Extensions.Configuration;

namespace Biolog.Features.OfflineSync.Domain;

/// <summary>
/// The real startup path for device credentials. Reads every
/// <c>OfflineSync:DeviceCredentials:&lt;deviceId&gt;</c> entry <see cref="IConfiguration"/>
/// exposes - ASP.NET Core's standard layered configuration, so an operator sets
/// <c>OfflineSync__DeviceCredentials__&lt;deviceId&gt;=&lt;secret&gt;</c> as an environment
/// variable (or <c>dotnet user-secrets</c> in Development) with no code change. No secret
/// value is committed to this repository. Mirrors BIO-PHO-002's own
/// <c>DeviceCredentialProvisioning</c> exactly, applied to a separate config section - this
/// feature owns its own device-credential namespace, the same "no cross-feature reference"
/// discipline that gives every sibling feature its own <c>IDeviceCredentialStore</c>.
/// </summary>
public static class DeviceCredentialProvisioning
{
    public const string ConfigurationSectionPath = "OfflineSync:DeviceCredentials";

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
