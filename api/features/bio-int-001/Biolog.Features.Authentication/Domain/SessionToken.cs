using System.Security.Cryptography;
using System.Text;

namespace Biolog.Features.Authentication;

/// <summary>
/// The result of a completed sign-in (BIO-INT-001's own end of the "provider abstraction" -
/// what a real caller receives once a provider adapter, claim mapping and any required MFA
/// have all succeeded). <see cref="Roles"/> are AC02-mapped role wire strings
/// (<see cref="KnownRoles"/>), never raw provider claims.
/// </summary>
public sealed record AuthenticatedPrincipal(
    string TenantId,
    string ProviderId,
    string SubjectId,
    string PersonId,
    IReadOnlySet<string> Roles,
    string SessionToken,
    DateTimeOffset ExpiresAt);

/// <summary>
/// Hand-rolled HMAC-signed opaque token - no JWT library dependency. Confirmed before
/// designing this (docs/features/bio-crd-007/README.md's own AC03 note): no JWT/session ADR
/// and no JWT dependency exists anywhere in this codebase today, so introducing one for a
/// foundation story would be a new, unreviewed dependency and vulnerability-scan surface for
/// a shape this codebase already has a precedent for solving without one - the exact HMAC
/// construction BIO-IDN-006's own <c>ActorProvenanceGate</c> uses (config-only secret, fails
/// closed to a dummy key when unconfigured, constant-time comparison). This token is
/// self-contained (tenant/provider/subject/person/roles/expiry are all embedded, verified by
/// signature) - there is no server-side session store to look up, matching the "opaque
/// caller-held reference, no product code parses it" shape a hand-rolled signed token needs
/// to stay safe without a real JWT library's structured-claims tooling.
///
/// This token is NOT wired into <c>ActorResolver</c>/request-authentication middleware
/// anywhere in this codebase - per ADR-0006 Decision 5, upgrading each existing story's actor
/// resolution from the asserted <c>X-Actor-Id</c> header to a real authenticated principal is
/// "a named follow-up on each story", not a retrofit this foundation story performs itself.
/// </summary>
public interface ISessionTokenCodec
{
    string Issue(string tenantId, string providerId, string subjectId, string personId, IReadOnlySet<string> roles, DateTimeOffset expiresAt);

    bool TryValidate(string token, out AuthenticatedPrincipal? principal);
}

public sealed class HmacSessionTokenCodec : ISessionTokenCodec
{
    public const string ConfigurationKey = "Authentication:SessionTokenSecret";
    public const int MinimumSecretLength = 32;

    private readonly byte[]? _key;

    /// <summary>TD-PROVENANCE-DUMMYKEY-SWEEP, normalization pass (Kemi's ruling, mirroring BIO-INT-003's own <c>HmacApiAccessTokenCodec</c>): no fallback key of any kind, and a secret shorter than <see cref="MinimumSecretLength"/> is rejected outright - an operator typo'ing a short/weak secret must not be silently accepted and used as a real, brute-forceable HMAC key. <see cref="Issue"/> throws and <see cref="TryValidate"/> always returns false when <c>_key</c> is null.</summary>
    public HmacSessionTokenCodec(string? configuredSecret)
    {
        _key = string.IsNullOrEmpty(configuredSecret) || configuredSecret.Length < MinimumSecretLength
            ? null
            : Encoding.UTF8.GetBytes(configuredSecret);
    }

    public string Issue(string tenantId, string providerId, string subjectId, string personId, IReadOnlySet<string> roles, DateTimeOffset expiresAt)
    {
        if (_key is null)
        {
            throw new InvalidOperationException(
                $"{ConfigurationKey} is not configured (or is shorter than {MinimumSecretLength} characters) - " +
                "refusing to issue a forgeable session token.");
        }

        var payload = string.Join('|', tenantId, providerId, subjectId, personId, string.Join(',', roles.OrderBy(r => r, StringComparer.Ordinal)), expiresAt.ToUnixTimeSeconds());
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        var signature = HMACSHA256.HashData(_key, payloadBytes);
        return Convert.ToBase64String(payloadBytes) + "." + Convert.ToHexString(signature);
    }

    public bool TryValidate(string token, out AuthenticatedPrincipal? principal)
    {
        principal = null;
        if (_key is null)
        {
            return false;
        }

        var parts = token.Split('.', 2);
        if (parts.Length != 2)
        {
            return false;
        }

        byte[] payloadBytes;
        byte[] presentedSignature;
        try
        {
            payloadBytes = Convert.FromBase64String(parts[0]);
            presentedSignature = Convert.FromHexString(parts[1]);
        }
        catch (FormatException)
        {
            return false;
        }

        var expectedSignature = HMACSHA256.HashData(_key, payloadBytes);
        if (!CryptographicOperations.FixedTimeEquals(presentedSignature, expectedSignature))
        {
            return false;
        }

        var fields = Encoding.UTF8.GetString(payloadBytes).Split('|');
        if (fields.Length != 6 || !long.TryParse(fields[5], out var expiresAtUnix))
        {
            return false;
        }

        var expiresAt = DateTimeOffset.FromUnixTimeSeconds(expiresAtUnix);
        if (expiresAt < DateTimeOffset.UtcNow)
        {
            return false;
        }

        var roles = fields[4].Length == 0
            ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>(fields[4].Split(','), StringComparer.Ordinal);

        principal = new AuthenticatedPrincipal(fields[0], fields[1], fields[2], fields[3], roles, token, expiresAt);
        return true;
    }
}
