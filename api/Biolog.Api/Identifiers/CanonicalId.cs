using System.Text.RegularExpressions;

namespace Biolog.Api.Identifiers;

/// <summary>
/// Canonical BIOLOG identifiers (ADR-0001 Decision 1): <c>&lt;type-prefix&gt;_&lt;uuidv7&gt;</c>,
/// the UUID lowercase and dash-separated. Only the six types ADR-0001 §1.2 registers are
/// exposed here - a story may not invent a prefix locally, so there is deliberately no
/// generic "mint with this prefix" escape hatch.
///
/// This is a minimal .NET port of what <c>contracts/js/identifiers.mjs</c> already gives
/// the web app: minting and shape validation for the identifiers this API actually needs
/// to produce today (<c>dev_</c> for BIO-DEV-001, <c>evt_</c> for the audit envelope this
/// story also emits). It does not port every registered type, and it is not the "shared
/// builder" a later story should still create for every .NET feature to depend on instead
/// of each one growing its own copy of this file.
/// </summary>
public static class CanonicalId
{
    private static readonly Regex Pattern = new(
        @"^(?<prefix>[a-z]{3})_[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$",
        RegexOptions.Compiled);

    public static string NewDeviceId() => New("dev");

    public static string NewEventId() => New("evt");

    private static string New(string prefix) => $"{prefix}_{Guid.CreateVersion7():D}";

    /// <summary>True if <paramref name="value"/> is a well-formed canonical id with the
    /// given <paramref name="expectedPrefix"/> - catching "a device id was passed where an
    /// event id was expected" at the boundary (ADR-0001 §1.4), not deep in a repository.</summary>
    public static bool IsWellFormed(string? value, string expectedPrefix)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        var match = Pattern.Match(value);
        return match.Success && match.Groups["prefix"].Value == expectedPrefix;
    }
}
