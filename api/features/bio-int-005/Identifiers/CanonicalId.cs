using System.Text.RegularExpressions;

namespace Biolog.Features.ImportConflictReview.Identifiers;

/// <summary>
/// Canonical BIOLOG identifiers (ADR-0001 Decision 1), scoped to the two REGISTERED types this
/// feature touches: <c>per_</c>/<c>ten_</c> (defense-in-depth shape validation only, the same
/// BIO-IDN-001-QA-01 discipline every sibling feature applies). A local, schema-conformant
/// helper - not the shared contract; mirrors <c>contracts/schemas/identifier-registry.json</c>
/// rather than referencing it, the same pattern every prior .NET feature uses.
///
/// <b>Deliberately does NOT mint a new registered identifier type for this story's own
/// aggregate</b> (<c>ImportConflictReview</c>, keyed by the natural triple
/// <c>(TenantId, MappingId, ExternalKey)</c> - see that type's own header). Adding a new
/// registered prefix to <c>contracts/schemas/identifier-registry.json</c> is Kemi-owned, out of
/// this story's authority.
/// </summary>
public static partial class CanonicalId
{
    public const string EventPrefix = "evt";

    [GeneratedRegex(@"^per_[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$")]
    private static partial Regex PersonIdPattern();

    [GeneratedRegex(@"^ten_[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$")]
    private static partial Regex TenantIdPattern();

    [GeneratedRegex(@"^evt_[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$")]
    private static partial Regex EventIdPattern();

    public static bool IsPersonId(string? value) => value is not null && PersonIdPattern().IsMatch(value);

    public static bool IsTenantId(string? value) => value is not null && TenantIdPattern().IsMatch(value);

    public static bool IsEventId(string? value) => value is not null && EventIdPattern().IsMatch(value);

    /// <summary>Mint a fresh <c>evt_</c> identifier (a registered canonical type) for an audit event or a minted correlation id.</summary>
    public static string NewEventId() => $"{EventPrefix}_{Guid.CreateVersion7():D}";
}
