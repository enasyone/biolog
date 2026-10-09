using System.Text.RegularExpressions;

namespace Biolog.Features.Integrations.Identifiers;

/// <summary>
/// Canonical BIOLOG identifiers (ADR-0001 Decision 1), scoped to the two REGISTERED types this
/// feature touches: <c>per_</c>/<c>ten_</c> (defense-in-depth shape validation only, the same
/// BIO-IDN-001-QA-01 discipline every sibling feature applies). A local, schema-conformant
/// helper - not the shared contract; mirrors <c>contracts/schemas/identifier-registry.json</c>
/// rather than referencing it, the same pattern every prior .NET feature uses.
///
/// <b>Deliberately does NOT mint a new registered identifier type for this story's own
/// aggregates</b> (<c>ImportSourceMapping</c>/<c>ImportBatch</c>/<c>ExternalIdentityLink</c>).
/// <c>contracts/schemas/identifier-registry.json</c> has no entry for any of them, and adding
/// one is Kemi-owned, out of this story's authority - the same no-canonical-prefix precedent
/// BIO-INT-001's own <c>AuthProviderConfig.ProviderId</c> and BIO-VIS-001's own
/// <c>VisitorRecord.Id</c> already established: a plain, undecorated UUIDv7 string
/// (<see cref="Guid.CreateVersion7"/>), never a made-up prefix.
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

    /// <summary>Mint a fresh, non-canonical-registry identity for this story's own aggregates - see class header.</summary>
    public static string NewLocalId() => Guid.CreateVersion7().ToString();

    /// <summary>Mint a fresh <c>evt_</c> identifier (a registered canonical type) for an audit event or a minted correlation id.</summary>
    public static string NewEventId() => $"{EventPrefix}_{Guid.CreateVersion7():D}";
}
