using Biolog.Features.OfflineSync.Domain;
using Biolog.Features.OfflineSync.Identifiers;
using Microsoft.AspNetCore.Http;

namespace Biolog.Features.OfflineSync.Api;

/// <summary>
/// Authenticates the mobile device an offline-sync submission comes from. Mirrors
/// BIO-PHO-002's own <c>HeaderDeviceAuthenticator</c> exactly: a request must present BOTH a
/// well-formed <c>X-Biolog-Device-Id</c> AND the matching <c>X-Biolog-Device-Secret</c> an
/// <see cref="IDeviceCredentialStore"/> was seeded with out of band
/// (<see cref="DeviceCredentialProvisioning.LoadFromConfiguration"/>). Every failure mode (no
/// id, no secret, malformed id, unregistered device, inactive device, wrong secret) returns
/// the identical generic <c>401 device_authentication_failed</c>.
///
/// Deliberately returns the resolved device (id + tenant), not just the id - unlike
/// BIO-PHO-002's own authenticator, this feature has no ambient tenant context to fall back on
/// (see <see cref="IDeviceTenantResolutionGateway"/>'s header): the tenant this request may act
/// on comes from nowhere else.
/// </summary>
public interface IDeviceAuthenticator
{
    Task<ResolvedDevice?> TryAuthenticateAsync(HttpRequest request, CancellationToken cancellationToken = default);
}

public sealed class HeaderDeviceAuthenticator(IDeviceCredentialStore credentialStore, IDeviceTenantResolutionGateway resolutionGateway) : IDeviceAuthenticator
{
    public const string DeviceIdHeaderName = "X-Biolog-Device-Id";
    public const string DeviceSecretHeaderName = "X-Biolog-Device-Secret";

    public async Task<ResolvedDevice?> TryAuthenticateAsync(HttpRequest request, CancellationToken cancellationToken = default)
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

        return await resolutionGateway.ResolveAsync(deviceId, cancellationToken);
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
