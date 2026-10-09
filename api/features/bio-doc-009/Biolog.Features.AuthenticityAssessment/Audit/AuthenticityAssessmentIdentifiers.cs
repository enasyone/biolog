using System.Globalization;
using System.Text.RegularExpressions;

namespace Biolog.Features.AuthenticityAssessment.Audit;

/// <summary>
/// Own local copy of the identically-shaped identifier/clock helpers every audited BIOLOG
/// feature carries (e.g. <c>Biolog.Features.EvidenceComparison.Audit.EvidenceComparisonEventId</c>).
/// Mints only the already-frozen <c>evt_</c> prefix (ADR-0001 S1.2's registry already registers it
/// for audit/correlation events) - this type registers no new identifier type of its own.
/// </summary>
public static partial class AuthenticityAssessmentEventId
{
    private const string EventPrefix = "evt";

    [GeneratedRegex(@"^evt_[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$")]
    private static partial Regex EventIdPattern();

    public static bool IsEventId(string? value) => value is not null && EventIdPattern().IsMatch(value);

    public static string New() => $"{EventPrefix}_{Guid.CreateVersion7():D}";
}

/// <summary>RFC 3339 UTC instant rendering (ADR-0001 Decision 2.1): literal <c>Z</c>, exactly
/// three fractional digits, always UTC.</summary>
public static class AuthenticityAssessmentClock
{
    private const string Format = "yyyy-MM-ddTHH:mm:ss.fffZ";

    public static string Now() => DateTime.UtcNow.ToString(Format, CultureInfo.InvariantCulture);
}
