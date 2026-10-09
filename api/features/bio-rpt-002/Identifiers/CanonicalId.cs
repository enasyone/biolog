using System.Text.RegularExpressions;

namespace Biolog.Features.ReportBuilder.Identifiers;

/// <summary>
/// Canonical BIOLOG identifiers (ADR-0001 Decision 1), scoped to the types this feature
/// touches: <c>ten_</c> (shape validation only) and <c>evt_</c> (audit/correlation events,
/// minted here). A report job is keyed by its own new <c>rpj_</c> prefix - a genuinely new kind
/// of durable object this codebase has not minted an id for before (a card issue/production job
/// reuse an existing natural composite key; a report job has no natural key of its own - it is
/// one ad hoc request, not an attempt against a pre-existing entity).
/// </summary>
public static partial class CanonicalId
{
    public const string EventPrefix = "evt";
    public const string ReportJobPrefix = "rpj";

    [GeneratedRegex(@"^ten_[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$")]
    private static partial Regex TenantIdPattern();

    [GeneratedRegex(@"^evt_[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$")]
    private static partial Regex EventIdPattern();

    [GeneratedRegex(@"^rpj_[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$")]
    private static partial Regex ReportJobIdPattern();

    public static bool IsTenantId(string? value) => value is not null && TenantIdPattern().IsMatch(value);

    public static bool IsEventId(string? value) => value is not null && EventIdPattern().IsMatch(value);

    public static bool IsReportJobId(string? value) => value is not null && ReportJobIdPattern().IsMatch(value);

    public static string NewEventId() => $"{EventPrefix}_{Guid.CreateVersion7():D}";

    public static string NewReportJobId() => $"{ReportJobPrefix}_{Guid.CreateVersion7():D}";
}
