using System.Text.RegularExpressions;

namespace Biolog.Features.Visitors.Identifiers;

/// <summary>
/// Canonical BIOLOG identifiers (ADR-0001 Decision 1), scoped to the two REGISTERED types this
/// feature touches: <c>per_</c>/<c>ten_</c> (defense-in-depth shape validation only, the same
/// BIO-IDN-001-QA-01 discipline every sibling feature applies). A local, schema-conformant
/// helper - not the shared contract; mirrors <c>contracts/schemas/identifier-registry.json</c>
/// rather than referencing it, the same pattern every prior <c>.NET</c> feature uses.
///
/// <b>Deliberately does NOT mint a new registered identifier type for <c>VisitorRecord</c>.</b>
/// <c>contracts/schemas/identifier-registry.json</c> is Kemi-owned ("Adding or changing an entry
/// requires an amending ADR and recorded Kemi approval") and has no <c>vis</c> entry. Rather than
/// unilaterally requesting one - a shared-contract change outside this story's authority - this
/// feature follows BIO-ACC-001's own disclosed precedent for the identical situation (it keyed
/// <c>AccessPolicy</c> by a business <c>PolicyKey</c> rather than mint a new canonical type): a
/// <see cref="VisitorRecord.Id"/> is a plain, undecorated UUIDv7 string
/// (<see cref="Guid.CreateVersion7"/>), never a <c>vis_</c>-prefixed value, so nothing in an
/// audit trail or API response could be mistaken for a registered BIOLOG identifier type it is
/// not. See <c>docs/features/bio-vis-001/README.md</c> "Why VisitorRecord has no canonical id
/// prefix" for the full record.
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

    /// <summary>Mint a fresh, non-canonical-registry visitor record identity - see class header.</summary>
    public static string NewVisitorId() => Guid.CreateVersion7().ToString();

    /// <summary>Mint a fresh <c>evt_</c> identifier (a registered canonical type) for an audit event or a minted correlation id.</summary>
    public static string NewEventId() => $"{EventPrefix}_{Guid.CreateVersion7():D}";
}
