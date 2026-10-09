using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Biolog.Features.OfflineEnrolment.Domain;

/// <summary>Per-device shared-secret verification for this feature's device authentication boundary - the same shape BIO-MOB-003/BIO-ACC-003 each establish. A device's real secret is provisioned out of band (<see cref="DeviceCredentialProvisioning"/>), never through this module's own HTTP surface.</summary>
public interface IDeviceCredentialStore
{
    bool Validate(string deviceId, string secret);
}

/// <summary>In-memory, process-lifetime credential store. Secrets are hashed (SHA-256) at rest and every comparison is constant-time against a hash - including for an unregistered device (a deterministic dummy hash), so response timing cannot distinguish "device does not exist" from "device exists, wrong secret." Mirrors BIO-MOB-003's/BIO-ACC-003's own copy exactly.</summary>
public sealed class InMemoryDeviceCredentialStore : IDeviceCredentialStore
{
    private readonly ConcurrentDictionary<string, byte[]> _hashesByDeviceId = new(StringComparer.Ordinal);
    private static readonly byte[] DummyHash = SHA256.HashData("dummy-hash-for-timing-defense-only"u8.ToArray());

    public void Register(string deviceId, string secret) =>
        _hashesByDeviceId[deviceId] = SHA256.HashData(Encoding.UTF8.GetBytes(secret));

    public bool Validate(string deviceId, string secret)
    {
        var expectedHash = _hashesByDeviceId.TryGetValue(deviceId, out var stored) ? stored : DummyHash;
        var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
        var matches = CryptographicOperations.FixedTimeEquals(expectedHash, suppliedHash);
        return matches && _hashesByDeviceId.ContainsKey(deviceId);
    }
}

/// <summary>Reads every <c>OfflineEnrolment:DeviceCredentials:&lt;deviceId&gt;</c> entry from configuration - an operator sets <c>OfflineEnrolment__DeviceCredentials__&lt;deviceId&gt;=&lt;secret&gt;</c> out of band, no code change, no secret committed to this repository. Own device-credential namespace, distinct from BIO-MOB-003's <c>OfflineSync:DeviceCredentials</c> and BIO-ACC-003's <c>AccessCache:DeviceCredentials</c> - a device authorised to submit offline sync mutations is not automatically authorised to submit an enrolment package.</summary>
public static class DeviceCredentialProvisioning
{
    public const string ConfigurationSectionPath = "OfflineEnrolment:DeviceCredentials";

    public static InMemoryDeviceCredentialStore LoadFromConfiguration(Microsoft.Extensions.Configuration.IConfiguration configuration)
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
