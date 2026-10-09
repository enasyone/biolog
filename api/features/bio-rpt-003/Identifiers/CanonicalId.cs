using System.Text.RegularExpressions;

namespace Biolog.Features.AuditInvestigation.Identifiers;

/// <summary>
/// Canonical BIOLOG identifiers (ADR-0001 Decision 1), scoped to the types this feature touches:
/// <c>ten_</c> (shape validation only), <c>per_</c> (a filter/permission-check input, shape
/// validation only) and <c>evt_</c> (minted only for this feature's OWN meta-audit trail - every
/// searched/exported record reuses its source feature's own evt_ id verbatim, never re-minted).
/// </summary>
public static partial class CanonicalId
{
    public const string EventPrefix = "evt";

    [GeneratedRegex(@"^ten_[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$")]
    private static partial Regex TenantIdPattern();

    [GeneratedRegex(@"^evt_[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$")]
    private static partial Regex EventIdPattern();

    [GeneratedRegex(@"^per_[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$")]
    private static partial Regex PersonIdPattern();

    public static bool IsTenantId(string? value) => value is not null && TenantIdPattern().IsMatch(value);

    public static bool IsEventId(string? value) => value is not null && EventIdPattern().IsMatch(value);

    public static bool IsPersonId(string? value) => value is not null && PersonIdPattern().IsMatch(value);

    public static string NewEventId() => $"{EventPrefix}_{Guid.CreateVersion7():D}";
}
