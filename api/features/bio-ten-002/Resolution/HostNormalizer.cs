using System.Text.RegularExpressions;

namespace Biolog.Features.TenantDomainRegistry.Resolution;

/// <summary>
/// BIO-TEN-002-AC02: "Host resolution is case-normalised and exact." ADR-0002 §1.3 is the
/// authority for exactly what "normalised" means here, and is deliberately narrow:
///
/// <list type="bullet">
/// <item>lowercase</item>
/// <item>trailing dot removed (at most one - a well-formed FQDN has at most one root dot;
/// anything left malformed after that is rejected by the shape check below, which is the
/// correct fail-closed outcome for a hostname that was never well-formed)</item>
/// <item>port removed (the <c>:port</c> a <c>Host</c> header may carry - this feature only
/// ever sees tenant custom domains, e.g. <c>portal.example.edu.ng</c>, never an IP-literal
/// host, so a bare <c>:</c> split is sufficient; no IPv6 bracket-literal handling is
/// implemented, matching <c>contracts/schemas/tenant-domain.schema.json</c>'s hostname
/// pattern, which has no IP-literal branch either)</item>
/// </list>
///
/// <b>BIO-TEN-002-QA-02 (Seyi round 1, High):</b> "port removed" only ever meant a SYNTACTIC
/// port - 1 to 5 ASCII digits - never "whatever follows the first colon". The original
/// implementation split on the first colon unconditionally, so <c>portal.example.edu.ng:evil</c>,
/// <c>portal.example.edu.ng:</c> (empty port) and <c>portal.example.edu.ng:443.attacker.invalid</c>
/// all silently aliased the verified hostname <c>portal.example.edu.ng</c> - a caller-controlled
/// suffix was being dropped rather than validated, exactly the "normalisation as convenience
/// instead of security boundary" failure ADR-0002 §1.3 warns against. A colon now only ever
/// removes a syntactically valid terminal port; anything else after a colon makes the whole
/// input rejected, not partially accepted.
///
/// Everything else is a REJECTION, not a further normalisation - most importantly, non-ASCII
/// input is rejected outright, never converted: "a resolver that quietly converts
/// <c>pаypal.example</c> [Cyrillic а] into <c>paypal.example</c> has built a homoglyph
/// confusion into the tenant boundary. Punycode conversion belongs at the edge, before this
/// contract sees the host" (ADR-0002 §1.3). This class is deliberately not that edge.
///
/// "Exact" (the second half of AC02) is enforced by construction: <see cref="Normalize"/>
/// returns the one canonical string a hostname maps to, or <c>null</c>; nothing downstream of
/// it does substring, suffix or wildcard matching against that string.
/// </summary>
public static partial class HostNormalizer
{
    // Mirrors contracts/schemas/tenant-domain.schema.json's `hostname` pattern exactly:
    // dot-separated DNS labels, each 1-63 chars, alphanumeric with internal hyphens only.
    [GeneratedRegex(@"^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?(\.[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?)*$")]
    private static partial Regex HostnamePattern();

    private const int MaxLength = 253;

    /// <summary>
    /// Returns the normalised, canonical hostname, or <c>null</c> if the input cannot be
    /// normalised into a well-formed one (empty, non-ASCII, malformed DNS-label shape, or too
    /// long). A <c>null</c> result is handled identically to "no such hostname" by every
    /// caller (BIO-TEN-002-AC03) - there is no separate "malformed" error path a caller could
    /// use to distinguish "not a real hostname" from "a real hostname nobody registered".
    /// </summary>
    public static string? Normalize(string? rawHost)
    {
        if (string.IsNullOrWhiteSpace(rawHost))
        {
            return null;
        }

        var host = rawHost.Trim();

        var colonIndex = host.IndexOf(':');
        if (colonIndex >= 0)
        {
            var portPart = host[(colonIndex + 1)..];
            // A syntactically valid port is 1-5 ASCII digits, 0-65535 - anything else (empty,
            // non-numeric, another colon, a trailing suffix) means the whole authority is
            // malformed, not that the extra text can be silently dropped (QA-02).
            if (portPart.Length is 0 or > 5 || !portPart.All(char.IsAsciiDigit)
                || !int.TryParse(portPart, out var port) || port is < 0 or > 65535)
            {
                return null;
            }

            host = host[..colonIndex];
        }

        if (host.EndsWith('.'))
        {
            host = host[..^1];
        }

        foreach (var c in host)
        {
            if (c > 0x7F)
            {
                return null; // non-ASCII: reject, never convert (ADR-0002 §1.3)
            }
        }

        var lower = host.ToLowerInvariant();
        if (lower.Length is 0 or > MaxLength || !HostnamePattern().IsMatch(lower))
        {
            return null;
        }

        return lower;
    }
}
