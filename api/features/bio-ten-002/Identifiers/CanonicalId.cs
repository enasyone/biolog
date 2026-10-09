using System.Text.RegularExpressions;

namespace Biolog.Features.TenantDomainRegistry.Identifiers;

/// <summary>
/// Canonical BIOLOG identifiers (ADR-0001 Decision 1), scoped to the two types this feature
/// touches: <c>ten_</c> (the tenant a domain is registered under - this feature validates the
/// shape of a caller-supplied tenant id, it never mints one; BIO-TEN-001 owns minting) and
/// <c>evt_</c> (audit/correlation events, ADR-0003 Decision 1, minted here). This is a local,
/// schema-conformant helper - not the shared contract. <c>contracts/js/identifiers.mjs</c> is
/// the one real registry and implementation; no generated .NET binding exists yet
/// (contracts/README.md: "Bindings for .NET ... are generated from the same schemas by the
/// stories that introduce those surfaces"). Every constant and regex below is cross-checked
/// against <c>contracts/schemas/identifier-registry.json</c> and <c>canonical-id.schema.json</c>
/// rather than invented, and this type registers no new identifier type of its own.
///
/// <see cref="Domain.TenantDomain"/> deliberately has no canonical id of its own kind (no
/// <c>dom_</c> prefix): see that type's header note for why.
/// </summary>
public static partial class CanonicalId
{
    public const string TenantPrefix = "ten";
    public const string EventPrefix = "evt";

    // RFC 9562 UUIDv7, lowercase, dashed: version nibble pinned to 7, variant nibble to
    // 0b10 (8/9/a/b) - contracts/schemas/canonical-id.schema.json's pattern, narrowed to the
    // two prefixes this feature uses.
    [GeneratedRegex(@"^ten_[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$")]
    private static partial Regex TenantIdPattern();

    [GeneratedRegex(@"^evt_[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$")]
    private static partial Regex EventIdPattern();

    public static bool IsTenantId(string? value) => value is not null && TenantIdPattern().IsMatch(value);

    public static bool IsEventId(string? value) => value is not null && EventIdPattern().IsMatch(value);

    /// <summary>Mint a fresh <c>evt_</c> identifier for an audit event or a minted correlation id.</summary>
    public static string NewEventId() => $"{EventPrefix}_{Guid.CreateVersion7():D}";
}
