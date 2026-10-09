using Microsoft.Extensions.Configuration;

namespace Biolog.Features.PhotoBooth.Domain;

/// <summary>
/// The real startup path for device credentials (BIO-PHO-002-AC02) - applying BIO-CRD-001-QA-02's
/// lesson from the start (round 1 of that story shipped an authenticator with no real
/// provisioning path at all, making every composed-host request permanently fail; this story
/// wires the fix's own shape in from day one). Reads every
/// <c>PhotoBooth:DeviceCredentials:&lt;deviceId&gt;</c> entry <see cref="IConfiguration"/> exposes -
/// ASP.NET Core's standard layered configuration, so an operator sets
/// <c>PhotoBooth__DeviceCredentials__&lt;deviceId&gt;=&lt;secret&gt;</c> as an environment variable (or
/// <c>dotnet user-secrets</c> in Development) with no code change. No secret value is committed
/// to this repository - neither <c>appsettings.json</c> nor <c>appsettings.Development.json</c>
/// declares this section, on purpose.
/// </summary>
public static class DeviceCredentialProvisioning
{
    public const string ConfigurationSectionPath = "PhotoBooth:DeviceCredentials";

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
