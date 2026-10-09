using Biolog.Features.CardLifecycle.Domain;
using Biolog.Features.CardLifecycle.Identifiers;
using Microsoft.AspNetCore.Http;

namespace Biolog.Features.CardLifecycle.Api;

/// <summary>
/// Authenticates the device a deny-list read comes from (TD-CRD-006-DENY-LIST-DEVICE-AUTH,
/// ADR-0002 Decision 7). Mirrors BIO-PHO-002's own <c>HeaderDeviceAuthenticator</c> exactly: a
/// request must present BOTH a well-formed <c>X-Biolog-Device-Id</c> AND the matching
/// <c>X-Biolog-Device-Secret</c> an <see cref="IDeviceCredentialStore"/> was seeded with out of
/// band (<see cref="DeviceCredentialProvisioning.LoadFromConfiguration"/>). The device must ALSO
/// exist and be <c>DeviceStatus.Active</c> in the resolved tenant
/// (<see cref="IDeviceExistenceGateway"/> - see that interface's own header for why this is
/// also where tenant binding is preserved). Every failure mode (no id, no secret, malformed id,
/// unregistered device, inactive device, wrong secret) returns the identical generic
/// <c>401 device_authentication_failed</c>, so a caller cannot use this endpoint to enumerate
/// which device ids exist, are active, or have a provisioned credential.
///
/// This closes GET /api/v1/card-lifecycle/deny-list's own gap: before this fix the endpoint had
/// no auth of any kind, not even the baseline asserted-actor check every other endpoint on this
/// route table requires - any caller who could reach the tenant's resolved host context could
/// pull every locked/permanently-revoked card record for that tenant. Still not an
/// authenticated-device PKI/mTLS scheme - a narrower, honestly-scoped property ("this caller
/// proved possession of a credential for this device id, and that device is a real, active,
/// registered device in this tenant"), the same disclosed scope boundary BIO-PHO-002/BIO-CRD-001
/// already carry.
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
