using Biolog.Features.MobileVerification.Domain;
using Biolog.Features.MobileVerification.Identifiers;
using Microsoft.AspNetCore.Http;

namespace Biolog.Features.MobileVerification.Api;

/// <summary>
/// Authenticates the mobile device a verification-decision request or history read comes from.
/// Mirrors BIO-MOB-003's/BIO-ACC-003's/BIO-MOB-005's own <c>HeaderDeviceAuthenticator</c> exactly:
/// a request must present BOTH a well-formed <c>X-Biolog-Device-Id</c> AND the matching
/// <c>X-Biolog-Device-Secret</c> an <see cref="IDeviceCredentialStore"/> was seeded with out of
/// band. Every failure mode returns the identical generic <c>401 device_authentication_failed</c>.
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
