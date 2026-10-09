using System.Text.RegularExpressions;

namespace Biolog.Features.CardTemplates.Identifiers;

/// <summary>
/// Canonical BIOLOG identifiers (ADR-0001 Decision 1), scoped to the types this feature
/// touches: <c>ten_</c> (shape validation only) and <c>evt_</c> (audit/correlation events,
/// minted here). A card template lineage is keyed by its own new <c>tpl_</c> prefix - it has no
/// pre-existing natural key in this codebase, unlike a production job (which reuses BIO-CRD-005's
/// own card-issue composite key).
/// </summary>
public static partial class CanonicalId
{
    public const string EventPrefix = "evt";
    public const string TemplatePrefix = "tpl";

    [GeneratedRegex(@"^ten_[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$")]
    private static partial Regex TenantIdPattern();

    [GeneratedRegex(@"^evt_[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$")]
    private static partial Regex EventIdPattern();

    [GeneratedRegex(@"^tpl_[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$")]
    private static partial Regex TemplateIdPattern();

    public static bool IsTenantId(string? value) => value is not null && TenantIdPattern().IsMatch(value);

    public static bool IsEventId(string? value) => value is not null && EventIdPattern().IsMatch(value);

    public static bool IsTemplateId(string? value) => value is not null && TemplateIdPattern().IsMatch(value);

    public static string NewEventId() => $"{EventPrefix}_{Guid.CreateVersion7():D}";

    public static string NewTemplateId() => $"{TemplatePrefix}_{Guid.CreateVersion7():D}";
}
