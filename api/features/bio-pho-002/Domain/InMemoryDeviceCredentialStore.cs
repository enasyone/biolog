using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Biolog.Features.PhotoBooth.Domain;

/// <summary>
/// In-memory, process-lifetime credential store for BIO-PHO-002-AC02. Secrets are hashed
/// (SHA-256) at rest, never stored in plaintext, and every comparison is constant-time
/// (<see cref="CryptographicOperations.FixedTimeEquals"/>) against a hash - including for an
/// UNREGISTERED device, which compares against a deterministic dummy hash rather than
/// short-circuiting, so a caller cannot use response timing to distinguish "device does not
/// exist" from "device exists, wrong secret." Mirrors <c>InMemoryTenantCredentialStore</c>
/// (BIO-CRD-001) exactly - the same shape, applied to a different id namespace.
/// </summary>
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
