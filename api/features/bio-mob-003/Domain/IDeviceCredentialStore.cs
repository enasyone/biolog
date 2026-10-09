namespace Biolog.Features.OfflineSync.Domain;

/// <summary>
/// Per-device shared-secret verification for BIO-MOB-003's device authentication boundary -
/// the same shape every sibling feature's own device/tenant credential store establishes
/// (BIO-CRD-001, BIO-PHO-002). A device's real secret is provisioned out of band
/// (<see cref="DeviceCredentialProvisioning"/>), never through this module's own HTTP surface.
/// </summary>
public interface IDeviceCredentialStore
{
    /// <summary>Constant-time comparison against the provisioned secret for <paramref name="deviceId"/>. False for an unregistered device or a wrong secret - indistinguishable from each other.</summary>
    bool Validate(string deviceId, string secret);
}
