using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Biolog.Features.CardProduction.Audit;

namespace Biolog.Features.CardProduction.Domain;

/// <summary>
/// BIO-CRD-007-AC03's own tenant-scoped signing key. In-memory, generated on first use per
/// tenant - the same "in-memory placeholder behind a real interface" seam ADR-0005 requires of
/// every story in this codebase, standing in for a real Key Vault/Managed HSM-backed key the
/// master specification's own security section names as the production target. Never returned
/// to a caller; only ever used server-side to sign and verify.
/// </summary>
public interface IProductionSigningKeyStore
{
    byte[] GetOrCreateKey(string tenantId);
}

internal sealed class InMemoryProductionSigningKeyStore : IProductionSigningKeyStore
{
    private readonly ConcurrentDictionary<string, byte[]> _keys = new(StringComparer.Ordinal);

    public byte[] GetOrCreateKey(string tenantId) =>
        _keys.GetOrAdd(tenantId, static _ => RandomNumberGenerator.GetBytes(32));
}

/// <summary>The claims a signed production package token carries - deliberately a local, minimal record, not a general-purpose JWT: this project has no existing JWT dependency, and a hand-rolled HMAC-SHA256 payload.signature token is the right-sized primitive for this story's own single, narrow need.</summary>
public sealed record ProductionPackagePayload
{
    [JsonPropertyName("package_id")]
    public required string PackageId { get; init; }

    [JsonPropertyName("tenant_id")]
    public required string TenantId { get; init; }

    [JsonPropertyName("person_id")]
    public required string PersonId { get; init; }

    [JsonPropertyName("card_class")]
    public required string CardClass { get; init; }

    [JsonPropertyName("sequence_number")]
    public required int SequenceNumber { get; init; }

    [JsonPropertyName("printer_ref")]
    public required string PrinterRef { get; init; }

    [JsonPropertyName("operator_actor_id")]
    public required string OperatorActorId { get; init; }

    [JsonPropertyName("operator_actor_type")]
    public required string OperatorActorType { get; init; }

    [JsonPropertyName("workstation_ref")]
    public required string WorkstationRef { get; init; }

    [JsonPropertyName("reprint_reason")]
    public string? ReprintReason { get; init; }

    [JsonPropertyName("issued_at")]
    public required string IssuedAt { get; init; }

    [JsonPropertyName("expires_at")]
    public required string ExpiresAt { get; init; }
}

/// <summary>
/// Builds and verifies BIO-CRD-007-AC03's signed, time-limited production-package token: a
/// base64url payload, a period, and a base64url HMAC-SHA256 signature over the exact payload
/// bytes - conceptually a JWT, deliberately hand-rolled rather than pulling in a general-purpose
/// JWT library for this one, narrow, project-local need.
/// </summary>
public static class ProductionPackageSigner
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = null };

    public static string BuildToken(ProductionPackage package, byte[] signingKey)
    {
        var payload = new ProductionPackagePayload
        {
            PackageId = package.PackageId,
            TenantId = package.TenantId,
            PersonId = package.PersonId,
            CardClass = package.CardClass,
            SequenceNumber = package.SequenceNumber,
            PrinterRef = package.PrinterRef,
            OperatorActorId = package.Operator.Id,
            OperatorActorType = package.Operator.Type.ToWireString(),
            WorkstationRef = package.WorkstationRef,
            ReprintReason = package.ReprintReason,
            IssuedAt = package.IssuedAt,
            ExpiresAt = package.ExpiresAt,
        };

        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        var payloadB64 = Base64UrlEncode(payloadBytes);
        var signatureB64 = Base64UrlEncode(ComputeSignature(payloadBytes, signingKey));
        return $"{payloadB64}.{signatureB64}";
    }

    /// <summary>
    /// Verifies the signature using <paramref name="signingKey"/> - the CALLER's own resolved
    /// tenant's key, never a key derived from the token's own embedded tenant_id - so a token
    /// forged for or replayed against the wrong tenant fails signature verification here rather
    /// than being distinguished by a separate, earlier tenant check (avoiding a needless
    /// unauthenticated-response oracle, ADR-0001 §1.5's own "no enumeration oracle" spirit).
    /// Returns the parsed payload only once the signature is confirmed valid.
    /// </summary>
    public static bool TryVerifyAndParse(string token, byte[] signingKey, out ProductionPackagePayload? payload)
    {
        payload = null;
        var parts = token.Split('.');
        if (parts.Length != 2)
        {
            return false;
        }

        byte[] payloadBytes;
        byte[] providedSignature;
        try
        {
            payloadBytes = Base64UrlDecode(parts[0]);
            providedSignature = Base64UrlDecode(parts[1]);
        }
        catch (FormatException)
        {
            return false;
        }

        var expectedSignature = ComputeSignature(payloadBytes, signingKey);
        if (providedSignature.Length != expectedSignature.Length ||
            !CryptographicOperations.FixedTimeEquals(providedSignature, expectedSignature))
        {
            return false;
        }

        try
        {
            payload = JsonSerializer.Deserialize<ProductionPackagePayload>(payloadBytes, JsonOptions);
        }
        catch (JsonException)
        {
            return false;
        }

        return payload is not null;
    }

    private static byte[] ComputeSignature(byte[] payloadBytes, byte[] signingKey)
    {
        using var hmac = new HMACSHA256(signingKey);
        return hmac.ComputeHash(payloadBytes);
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - padded.Length % 4) % 4);
        return Convert.FromBase64String(padded);
    }
}
