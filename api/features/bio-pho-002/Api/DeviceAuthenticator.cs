using Biolog.Features.PhotoBooth.Domain;
using Biolog.Features.PhotoBooth.Identifiers;
using Microsoft.AspNetCore.Http;

namespace Biolog.Features.PhotoBooth.Api;

/// <summary>
/// Authenticates the photo booth device a capture submission comes from (BIO-PHO-002-AC02).
///
/// <b>Applying BIO-CRD-001-QA-01/QA-02's lessons from the start.</b> BIO-CRD-001's own tenant
/// authenticator shipped round 1 trusting a bare, unverified id header - any caller could claim
/// any tenant - and round 1's fix then shipped with no real provisioning path, making every
/// composed-host request fail. Both are avoided here from day one: a request must present BOTH
/// a well-formed <c>X-Biolog-Device-Id</c> AND the matching <c>X-Biolog-Device-Secret</c> a
/// <see cref="IDeviceCredentialStore"/> was seeded with out of band
/// (<see cref="DeviceCredentialProvisioning.LoadFromConfiguration"/>, wired into
/// <c>BioPho002Module.RegisterServices</c> as the real startup path from the start) - never
/// through this module's own HTTP surface. The device must ALSO exist and be
/// <c>DeviceStatus.Active</c> in the resolved tenant (<see cref="IDeviceExistenceGateway"/>) -
/// a valid secret for a retired or not-yet-activated device does not authenticate. Every
/// failure mode (no id, no secret, malformed id, unregistered device, inactive device, wrong
/// secret) returns the identical generic <c>401 device_authentication_failed</c>, so a caller
/// cannot use this endpoint to enumerate which device ids exist, are active, or have a
/// provisioned credential.
///
/// This is still NOT an authenticated-device PKI/mTLS scheme - a narrower, honestly-scoped
/// property ("this caller proved possession of a credential for this device id, and that device
/// is a real, active, registered device"), matching BIO-CRD-001's own disclosed scope boundary
/// for tenant authentication.
/// </summary>
public interface IDeviceAuthenticator
{
    /// <summary>Returns the authenticated device id, or null if authentication failed for any reason.</summary>
    Task<string?> TryAuthenticateAsync(HttpRequest request, CancellationToken cancellationToken = default);
}

public sealed class HeaderDeviceAuthenticator(IDeviceCredentialStore credentialStore, IDeviceExistenceGateway deviceGateway) : IDeviceAuthenticator
{
    public const string DeviceIdHeaderName = "X-Biolog-Device-Id";
    public const string DeviceSecretHeaderName = "X-Biolog-Device-Secret";

    public async Task<string?> TryAuthenticateAsync(HttpRequest request, CancellationToken cancellationToken = default)
    {
        var deviceId = SingleHeaderValue(request, DeviceIdHeaderName);
        var secret = SingleHeaderValue(request, DeviceSecretHeaderName);
        if (deviceId is null || secret is null || !CanonicalId.IsDeviceId(deviceId))
        {
            return null;
        }

        // Validate is called even when the id is well-formed but unregistered - the store's
        // own constant-time-with-a-dummy-hash behaviour is what keeps this branch and the
        // "known device, wrong secret" branch indistinguishable by timing.
        if (!credentialStore.Validate(deviceId, secret))
        {
            return null;
        }

        return await deviceGateway.IsActiveAsync(deviceId, cancellationToken) ? deviceId : null;
    }

    private static string? SingleHeaderValue(HttpRequest request, string headerName)
    {
        if (!request.Headers.TryGetValue(headerName, out var values))
        {
            return null;
        }
        var value = values.ToString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
