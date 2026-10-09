namespace Biolog.Api.Observability;

/// <summary>
/// Name-based redaction for structured log values (BIO-FND-009-AC04). A log statement's
/// state and scope are key/value pairs supplied by whatever code called
/// <c>ILogger.Log*</c>/<c>BeginScope</c>; this checks each key's NAME against the "secret"
/// and "sensitive" examples in <c>contracts/schemas/data-classification.json</c> (ADR-0003
/// Decision 3, Kemi's shared registry) so a value never reaches a log sink just because a
/// caller forgot to scrub it first.
///
/// This is deliberately a denylist, unlike ADR-0003's own audit-envelope allowlist: an
/// audit envelope is built once, centrally, from a known field set, so an allowlist is
/// cheap there. A log statement can appear at any call site with an arbitrary message
/// template, so an allowlist would have to be maintained per call site or would block
/// almost everything by default - neither is a "baseline" a later story can build on. The
/// trade-off this accepts, and the reason it is a default rather than the only protection:
/// a field named something this list does not recognise still gets logged in full. Callers
/// that handle a classified value with a name outside this list must scrub it themselves;
/// this catches the common, known-name cases so they are excluded without every caller
/// having to remember to.
/// </summary>
public static class SensitiveLogKeys
{
    public const string RedactedValue = "[REDACTED]";

    /// <summary>
    /// Case-insensitive substrings matched against a structured log key. Sourced from
    /// <c>contracts/schemas/data-classification.json</c>'s "secret" examples (biometric
    /// template/image, card key, signing key, password, token) plus the closely related
    /// terms a real call site is likely to spell them with.
    /// </summary>
    private static readonly string[] BlockedSubstrings =
    [
        "password",
        "secret",
        "token",
        "credential",
        "signingkey",
        "signing_key",
        "cardkey",
        "card_key",
        "biometric",
        "privatekey",
        "private_key",
        "connectionstring",
        "connection_string",
        "apikey",
        "api_key",
        "authorization",
    ];

    /// <summary>
    /// True if <paramref name="key"/> matches a blocked name. <c>{OriginalFormat}</c> (the
    /// framework's own template-holder key) and a null/empty key are never blocked - there
    /// is nothing named there to match.
    /// </summary>
    public static bool IsSensitive(string? key)
    {
        if (string.IsNullOrEmpty(key) || key == "{OriginalFormat}")
        {
            return false;
        }

        foreach (var blocked in BlockedSubstrings)
        {
            if (key.Contains(blocked, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
