namespace Biolog.Features.CardLifecycle.Domain;

/// <summary>
/// Per-device shared-secret verification (TD-CRD-006-DENY-LIST-DEVICE-AUTH, ADR-0002 Decision 7).
/// Mirrors BIO-PHO-002's own <c>IDeviceCredentialStore</c> exactly - the same shape already
/// proven for a device-authenticated, no-ambient-actor surface. A device's real secret is
/// provisioned out of band (<see cref="DeviceCredentialProvisioning"/>), never through this
/// module's own HTTP surface.
/// </summary>
public interface IDeviceCredentialStore
{
    /// <summary>Constant-time comparison against the provisioned secret for <paramref name="deviceId"/>. False for an unregistered device or a wrong secret - indistinguishable from each other.</summary>
    bool Validate(string deviceId, string secret);
}
