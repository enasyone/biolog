namespace Biolog.Features.PhotoBooth.Domain;

/// <summary>
/// Per-device shared-secret verification for BIO-PHO-002-AC02 - the same shape
/// <c>ITenantCredentialStore</c> (BIO-CRD-001) established after that story's own QA-01 review
/// found a bare, unverified id header let any caller claim any tenant; applied here from the
/// start rather than waiting for the identical finding against a device id. A device's real
/// secret is provisioned out of band (<see cref="DeviceCredentialProvisioning"/>), never through
/// this module's own HTTP surface.
/// </summary>
public interface IDeviceCredentialStore
{
    /// <summary>Constant-time comparison against the provisioned secret for <paramref name="deviceId"/>. False for an unregistered device or a wrong secret - indistinguishable from each other.</summary>
    bool Validate(string deviceId, string secret);
}
