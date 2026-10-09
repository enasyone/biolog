using System.Security.Cryptography;
using System.Text;

namespace Biolog.Features.Webhooks;

/// <summary>
/// BIO-INT-004-AC03: signature AND replay protection, both from one construction - the same
/// "timestamp bound into the signed message" shape real-world webhook providers (Stripe,
/// GitHub) use, not invented here. The signed message is <c>"{timestampMs}.{body}"</c>, so a
/// captured, otherwise-valid signature cannot be replayed with a different (or absent)
/// timestamp without invalidating it - a receiver independently verifying this signature
/// MUST also reject any timestamp outside its own tolerance window, which this delivery
/// header makes possible (the signature itself proves the timestamp was genuinely part of
/// what was signed, not a value the deliverer could swap in afterward).
///
/// Delivery-side dedup (never redelivering the SAME event to the SAME subscription twice -
/// <see cref="WebhookDispatchService"/>'s own audit-sink check) is the other half of replay
/// protection: even a captured valid signature+timestamp pair corresponds to an event this
/// subscription will only ever have been sent once from our own side.
/// </summary>
public static class WebhookSigner
{
    /// <summary>Format: <c>t={unixMs},v1={hex-hmac}</c> - the exact header value a receiver parses.</summary>
    public static string ComputeSignatureHeader(string signingSecretHex, string body, DateTimeOffset timestamp)
    {
        var timestampMs = timestamp.ToUnixTimeMilliseconds();
        var signedMessage = $"{timestampMs}.{body}";
        var key = Encoding.UTF8.GetBytes(signingSecretHex);
        var signature = Convert.ToHexStringLower(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(signedMessage)));
        return $"t={timestampMs},v1={signature}";
    }
}
