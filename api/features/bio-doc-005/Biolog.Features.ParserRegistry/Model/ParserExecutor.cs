using System.Text;
using System.Text.RegularExpressions;

namespace Biolog.Features.ParserRegistry.Model;

/// <summary>
/// Applies a published parser version's field mapping and validation rules to a raw decoded
/// barcode payload (BIO-DOC-005-AC03), closing the loop from BIO-DOC-003's
/// <c>BarcodeDecodeAttempt.RawPayload</c> to structured field output. Only ever runs against a
/// <see cref="ParserStatus.Published"/> version - a Draft or Tested one has not been through
/// approval and must never be used to interpret a real payload.
///
/// <para>
/// Seyi's independent review (round 1, BIO-DOC-005-F01, High) found that <see cref="Validate"/>
/// called <c>Regex.IsMatch</c> with a tenant-authored <see cref="ParserValidationRule.Pattern"/>
/// and no timeout: a permitted tenant (or anyone who can reach a published parser's validation
/// with attacker-controlled raw payload bytes - the review noted both are reachable here) can
/// store a catastrophic-backtracking expression (e.g. <c>^(a+)+$</c>) that, matched against a
/// crafted long input, makes .NET's backtracking regex engine consume unbounded CPU - a denial of
/// service. Fixed with a bounded <see cref="MatchTimeout"/> on every match attempt, with a timeout
/// treated as an honest, distinct validation failure (<c>pattern_timeout</c>) rather than letting
/// <see cref="RegexMatchTimeoutException"/> propagate uncaught or silently treating it as a match.
/// </para>
/// </summary>
public static class ParserExecutor
{
    /// <summary>The regex engine's own recommended mitigation for a caller-supplied pattern
    /// matched against caller-supplied input (see the .NET docs on `Regex` and ReDoS): a bounded
    /// deadline the backtracking engine cannot exceed, regardless of how pathological the
    /// pattern/input combination is.</summary>
    public static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(250);

    public static ParserExecutionResult Apply(ParserVersionContent version, byte[] rawPayload)
    {
        ArgumentNullException.ThrowIfNull(rawPayload);
        if (version.Status != ParserStatus.Published)
        {
            throw new InvalidOperationException("only_a_published_parser_version_may_be_applied");
        }

        var validationByField = version.ValidationRules.ToDictionary(r => r.FieldName);
        var fields = new List<ParserFieldResult>();

        foreach (var mapping in version.FieldMappings)
        {
            var withinBounds = mapping.ByteOffset + mapping.Length <= rawPayload.Length;
            var rawValue = withinBounds
                ? Encoding.UTF8.GetString(rawPayload, mapping.ByteOffset, mapping.Length)
                : string.Empty;

            validationByField.TryGetValue(mapping.FieldName, out var rule);
            var (valid, failureReason) = Validate(withinBounds, rawValue, rule);

            fields.Add(new ParserFieldResult(mapping.FieldName, rawValue, valid, failureReason));
        }

        return new ParserExecutionResult(fields, fields.All(f => f.Valid));
    }

    private static (bool Valid, string? FailureReason) Validate(bool withinBounds, string rawValue, ParserValidationRule? rule)
    {
        if (!withinBounds)
        {
            return (false, "field_out_of_bounds");
        }
        if (rule is { Required: true } && string.IsNullOrWhiteSpace(rawValue))
        {
            return (false, "required_field_missing");
        }
        if (rule?.Pattern is not null)
        {
            bool matched;
            try
            {
                matched = Regex.IsMatch(rawValue, rule.Pattern, RegexOptions.None, MatchTimeout);
            }
            catch (RegexMatchTimeoutException)
            {
                // Never surfaced as a crash and never silently treated as a pass - a pattern that
                // cannot complete within the bounded deadline is reported for exactly what it is.
                return (false, "pattern_timeout");
            }

            if (!matched)
            {
                return (false, "pattern_mismatch");
            }
        }

        return (true, null);
    }
}
