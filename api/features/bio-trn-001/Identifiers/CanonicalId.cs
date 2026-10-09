using System.Text.RegularExpressions;

namespace Biolog.Features.Transport.Identifiers;

/// <summary>
/// Canonical BIOLOG identifiers (ADR-0001 Decision 1), scoped to the REGISTERED types this
/// feature touches: <c>per_</c>/<c>ten_</c>/<c>dev_</c> (defense-in-depth shape validation only,
/// the same BIO-IDN-001-QA-01 discipline every sibling feature applies). A local,
/// schema-conformant helper - not the shared contract; mirrors
/// <c>contracts/schemas/identifier-registry.json</c> rather than referencing it, the same pattern
/// every prior <c>.NET</c> feature uses.
///
/// <b>Deliberately does NOT mint new registered identifier types for Route/Vehicle/Stop/
/// PersonAssignment.</b> <c>contracts/schemas/identifier-registry.json</c> is Kemi-owned ("Adding
/// or changing an entry requires an amending ADR and recorded Kemi approval") and has no
/// <c>route</c>/<c>vehicle</c>/<c>stop</c>/<c>assignment</c> entry. Rather than unilaterally
/// requesting four - a shared-contract change outside this story's authority - this feature
/// follows BIO-ACC-001's (<c>PolicyKey</c>) and BIO-VIS-001's (<c>VisitorRecord.Id</c>) own
/// disclosed precedent for the identical situation: every one of this feature's four aggregate
/// ids is a plain, undecorated UUIDv7 string (<see cref="Guid.CreateVersion7"/>), never a
/// registry-prefixed value, so nothing in an audit trail or API response could be mistaken for a
/// registered BIOLOG identifier type it is not. See <c>docs/features/bio-trn-001/README.md</c>
/// "Why Route/Vehicle/Stop/PersonAssignment have no canonical id prefix" for the full record.
/// </summary>
public static partial class CanonicalId
{
    public const string EventPrefix = "evt";

    [GeneratedRegex(@"^per_[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$")]
    private static partial Regex PersonIdPattern();

    [GeneratedRegex(@"^ten_[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$")]
    private static partial Regex TenantIdPattern();

    [GeneratedRegex(@"^dev_[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$")]
    private static partial Regex DeviceIdPattern();

    [GeneratedRegex(@"^evt_[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$")]
    private static partial Regex EventIdPattern();

    public static bool IsPersonId(string? value) => value is not null && PersonIdPattern().IsMatch(value);

    public static bool IsTenantId(string? value) => value is not null && TenantIdPattern().IsMatch(value);

    public static bool IsDeviceId(string? value) => value is not null && DeviceIdPattern().IsMatch(value);

    public static bool IsEventId(string? value) => value is not null && EventIdPattern().IsMatch(value);

    /// <summary>Mint a fresh, non-canonical-registry aggregate identity for any of this feature's four record types - see class header.</summary>
    public static string NewLocalId() => Guid.CreateVersion7().ToString();

    /// <summary>Mint a fresh <c>evt_</c> identifier (a registered canonical type) for an audit event or a minted correlation id.</summary>
    public static string NewEventId() => $"{EventPrefix}_{Guid.CreateVersion7():D}";
}
