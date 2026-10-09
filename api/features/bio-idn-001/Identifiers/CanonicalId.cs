using System.Text.RegularExpressions;

namespace Biolog.Features.PersonRegistry.Identifiers;

/// <summary>
/// Canonical BIOLOG identifiers (ADR-0001 Decision 1), scoped to the three types this feature
/// touches: <c>per_</c> (the universal PERSON aggregate - minted here, this feature's own
/// entity), <c>ten_</c> (defense-in-depth shape validation only, BIO-IDN-001-QA-01 - see
/// <see cref="IsTenantId"/>) and <c>evt_</c> (audit/correlation events, ADR-0003 Decision 1,
/// minted here). This is a local, schema-conformant helper - not the shared contract.
/// <c>contracts/js/identifiers.mjs</c> is the one real registry and implementation; no
/// generated .NET binding exists yet (contracts/README.md: "Bindings for .NET ... are
/// generated from the same schemas by the stories that introduce those surfaces"). Every
/// constant and regex below is cross-checked against
/// <c>contracts/schemas/identifier-registry.json</c> and <c>canonical-id.schema.json</c>
/// rather than invented, and this type registers no new identifier type of its own.
///
/// This feature never MINTS a <c>ten_</c> tenant identifier: PERSON's tenant id always comes
/// from the resolved <see cref="Tenancy.ICurrentTenantProvider"/> - a value this process
/// itself already validated when it was resolved (BIO-TEN-002's real host channel), never
/// from request input.
/// </summary>
public static partial class CanonicalId
{
    public const string PersonPrefix = "per";
    public const string EventPrefix = "evt";

    // RFC 9562 UUIDv7, lowercase, dashed: version nibble pinned to 7, variant nibble to
    // 0b10 (8/9/a/b) - contracts/schemas/canonical-id.schema.json's pattern, narrowed to the
    // two prefixes this feature uses.
    [GeneratedRegex(@"^per_[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$")]
    private static partial Regex PersonIdPattern();

    [GeneratedRegex(@"^ten_[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$")]
    private static partial Regex TenantIdPattern();

    [GeneratedRegex(@"^evt_[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$")]
    private static partial Regex EventIdPattern();

    public static bool IsPersonId(string? value) => value is not null && PersonIdPattern().IsMatch(value);

    /// <summary>
    /// BIO-IDN-001-QA-01 (Seyi round 1): defense-in-depth shape check used only inside
    /// <see cref="Domain.Person"/>'s internal constructor, mirroring BIO-TEN-002-QA-03's
    /// identical reasoning for <c>TenantContext</c> - the real forgery path is closed by
    /// construction being <c>internal</c>, not by this string check, but this still catches
    /// an internal bug (e.g. passing the wrong variable) mistaking a different identifier
    /// type for a tenant id.
    /// </summary>
    public static bool IsTenantId(string? value) => value is not null && TenantIdPattern().IsMatch(value);

    public static bool IsEventId(string? value) => value is not null && EventIdPattern().IsMatch(value);

    /// <summary>Mint a fresh <c>per_</c> identifier for a newly registered PERSON.</summary>
    public static string NewPersonId() => $"{PersonPrefix}_{Guid.CreateVersion7():D}";

    /// <summary>Mint a fresh <c>evt_</c> identifier for an audit event or a minted correlation id.</summary>
    public static string NewEventId() => $"{EventPrefix}_{Guid.CreateVersion7():D}";
}
