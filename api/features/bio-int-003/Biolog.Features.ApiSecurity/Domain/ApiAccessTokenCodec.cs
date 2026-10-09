using System.Security.Cryptography;
using System.Text;

namespace Biolog.Features.ApiSecurity;

/// <summary>
/// The result of a completed OAuth2 client_credentials grant (BIO-INT-003-AC02).
/// <see cref="Scopes"/> are the granted, token-bound role-shaped scopes (<see cref="KnownRoles"/>) -
/// what a protected resource endpoint checks a bearer token against, never the account's full
/// granted-scope set if the caller requested a narrower subset.
/// </summary>
public sealed record ApiAccessToken(
    string TenantId,
    string ClientId,
    IReadOnlySet<string> Scopes,
    string AccessToken,
    DateTimeOffset ExpiresAt);

/// <summary>
/// Hand-rolled HMAC-signed opaque bearer token. No JWT library dependency - confirmed before
/// designing (no JWT/session ADR or dependency exists anywhere in this codebase, per BIO-INT-001's
/// own csproj comment and docs/features/bio-crd-007's README). This token is self-contained
/// (tenant/client/scopes/expiry are embedded, verified by signature) - there is no server-side
/// token store to look up.
///
/// <b>Security-review fix (Zainab, ZB-INT003-01, Critical):</b> this codec does NOT fall back to
/// a dummy/public key when unconfigured, unlike BIO-INT-001's own <c>HmacSessionTokenCodec</c>
/// and BIO-IDN-006's <c>ActorProvenanceGate</c> precedent (both reviewed and accepted with that
/// fallback - a narrower, already-disclosed gap this story does not retrofit, per the usual
/// single-story blast radius discipline). Zainab's review proved a real, concrete exploit this
/// story's own token actually enables that those two never did: two independently-unconfigured
/// instances would accept each other's tokens, so ANY deployment that forgot to configure a
/// secret - not merely one with a leaked key - would let any caller forge a token for any
/// tenant/client/scope combination, because the fallback key is a literal public string anyone
/// who reads this file already knows. <see cref="Issue"/> throws and <see cref="TryValidate"/>
/// always returns <c>false</c> when unconfigured (or configured with a secret shorter than
/// <see cref="MinimumSecretLength"/>) - this codec can never issue OR accept a token until a
/// real secret is configured, closing the fallback entirely rather than narrowing it.
///
/// This is a SEPARATE HMAC key from BIO-INT-001's session-token secret and from
/// <see cref="ApiSecretHasher"/>'s pepper - a leaked API access-token key must not also expose
/// human sign-in sessions or let an attacker forge a service-account secret hash.
/// </summary>
public interface IApiAccessTokenCodec
{
    string Issue(string tenantId, string clientId, IReadOnlySet<string> scopes, DateTimeOffset expiresAt);

    bool TryValidate(string token, out ApiAccessToken? accessToken);
}

public sealed class HmacApiAccessTokenCodec : IApiAccessTokenCodec
{
    public const string ConfigurationKey = "ApiSecurity:AccessTokenSecret";
    public const int MinimumSecretLength = 32;

    private readonly byte[]? _key;

    public HmacApiAccessTokenCodec(string? configuredSecret)
    {
        _key = string.IsNullOrEmpty(configuredSecret) || configuredSecret.Length < MinimumSecretLength
            ? null
            : Encoding.UTF8.GetBytes(configuredSecret);
    }

    public string Issue(string tenantId, string clientId, IReadOnlySet<string> scopes, DateTimeOffset expiresAt)
    {
        if (_key is null)
        {
            throw new InvalidOperationException(
                $"{ConfigurationKey} is not configured (or is shorter than {MinimumSecretLength} characters) - " +
                "refusing to issue a forgeable access token.");
        }

        var payload = string.Join('|', tenantId, clientId, string.Join(',', scopes.OrderBy(s => s, StringComparer.Ordinal)), expiresAt.ToUnixTimeSeconds());
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        var signature = HMACSHA256.HashData(_key, payloadBytes);
        return Convert.ToBase64String(payloadBytes) + "." + Convert.ToHexString(signature);
    }

    public bool TryValidate(string token, out ApiAccessToken? accessToken)
    {
        accessToken = null;
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
        if (fields.Length != 4 || !long.TryParse(fields[3], out var expiresAtUnix))
        {
            return false;
        }

        var expiresAt = DateTimeOffset.FromUnixTimeSeconds(expiresAtUnix);
        if (expiresAt < DateTimeOffset.UtcNow)
        {
            return false;
        }

        var scopes = fields[2].Length == 0
            ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>(fields[2].Split(','), StringComparer.Ordinal);

        accessToken = new ApiAccessToken(fields[0], fields[1], scopes, token, expiresAt);
        return true;
    }
}

/// <summary>
/// BIO-INT-003-AC04: hashes a service-account client secret under a server-side pepper before
/// it is ever persisted (<see cref="ServiceAccount.SecretHash"/>) - the plaintext itself is
/// never stored, logged, or written to any audit event. A separate HMAC key from
/// <see cref="HmacApiAccessTokenCodec"/>'s token-signing key (see that type's own remarks).
/// Comparison is constant-time (<see cref="Verify"/>) to avoid a timing side-channel on secret
/// verification at the OAuth2 token endpoint.
///
/// <b>Security-review fix (Zainab, ZB-INT003-01, Critical):</b> same fix as
/// <see cref="HmacApiAccessTokenCodec"/> and for the same reason - no dummy-pepper fallback.
/// <see cref="Hash"/> throws and <see cref="Verify"/> always returns <c>false</c> when
/// unconfigured (or shorter than <see cref="MinimumPepperLength"/>). Zainab's review noted the
/// 256-bit generated client secret limits immediate exploitation of a weak/missing pepper on its
/// own, but required the same fail-closed discipline regardless - a missing pepper must not
/// silently downgrade the at-rest control to a public, guessable HMAC key.
/// </summary>
public interface IApiSecretHasher
{
    byte[] Hash(string plaintextSecret);

    bool Verify(string plaintextSecret, byte[] storedHash);
}

public sealed class HmacApiSecretHasher : IApiSecretHasher
{
    public const string ConfigurationKey = "ApiSecurity:SecretHashPepper";
    public const int MinimumPepperLength = 32;

    private readonly byte[]? _pepper;

    public HmacApiSecretHasher(string? configuredPepper)
    {
        _pepper = string.IsNullOrEmpty(configuredPepper) || configuredPepper.Length < MinimumPepperLength
            ? null
            : Encoding.UTF8.GetBytes(configuredPepper);
    }

    public byte[] Hash(string plaintextSecret)
    {
        if (_pepper is null)
        {
            throw new InvalidOperationException(
                $"{ConfigurationKey} is not configured (or is shorter than {MinimumPepperLength} characters) - " +
                "refusing to hash a client secret under a public, guessable key.");
        }

        return HMACSHA256.HashData(_pepper, Encoding.UTF8.GetBytes(plaintextSecret));
    }

    public bool Verify(string plaintextSecret, byte[] storedHash) =>
        _pepper is not null && CryptographicOperations.FixedTimeEquals(Hash(plaintextSecret), storedHash);
}

/// <summary>
/// Generates the one-time plaintext client secret shown to the caller at create/rotate time
/// (BIO-INT-003-AC04) - cryptographically random, never derived from any public value (the
/// same lesson BIO-INT-001-F01 established for MFA challenge codes: a secret must never be
/// computable from something the response itself also reveals, such as the account id).
/// </summary>
public static class ApiSecretGenerator
{
    public static string Generate() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
}
