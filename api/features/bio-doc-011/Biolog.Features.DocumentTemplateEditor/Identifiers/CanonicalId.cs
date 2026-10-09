using System.Text.RegularExpressions;

namespace Biolog.Features.DocumentTemplateEditor.Identifiers;

/// <summary>
/// Canonical BIOLOG identifiers (ADR-0001 Decision 1), scoped to the types this feature touches:
/// <c>ten_</c> (shape validation only) and <c>evt_</c> (audit/correlation events, minted here).
/// A tenant document-template-draft lineage is keyed by its own new <c>dtd_</c> prefix - it has
/// no pre-existing natural key in this codebase, the same reasoning BIO-CRD-008's own <c>tpl_</c>
/// prefix used for the identical situation.
/// </summary>
public static partial class CanonicalId
{
    public const string EventPrefix = "evt";
    public const string DraftPrefix = "dtd";

    [GeneratedRegex(@"^ten_[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$")]
    private static partial Regex TenantIdPattern();

    [GeneratedRegex(@"^evt_[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$")]
    private static partial Regex EventIdPattern();

    [GeneratedRegex(@"^dtd_[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$")]
    private static partial Regex DraftIdPattern();

    public static bool IsTenantId(string? value) => value is not null && TenantIdPattern().IsMatch(value);

    public static bool IsEventId(string? value) => value is not null && EventIdPattern().IsMatch(value);

    public static bool IsDraftId(string? value) => value is not null && DraftIdPattern().IsMatch(value);

    public static string NewEventId() => $"{EventPrefix}_{Guid.CreateVersion7():D}";

    public static string NewDraftId() => $"{DraftPrefix}_{Guid.CreateVersion7():D}";
}
