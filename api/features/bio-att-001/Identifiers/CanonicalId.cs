using System.Text.RegularExpressions;

namespace Biolog.Features.Attendance.Identifiers;

/// <summary>
/// Canonical BIOLOG identifiers (ADR-0001 Decision 1), scoped to the four types this feature
/// touches: <c>evt_</c> (the raw attendance event itself - ADR-0003 Decision 1's "domain event"
/// type, minted here), <c>per_</c>/<c>ten_</c>/<c>dev_</c> (defense-in-depth shape validation
/// only - the same BIO-IDN-001-QA-01 discipline every sibling feature applies at its own
/// aggregate boundary). A local, schema-conformant helper - not the shared contract; mirrors
/// <c>contracts/schemas/identifier-registry.json</c> rather than referencing it, the same
/// pattern every prior <c>.NET</c> feature in this codebase uses (no generated binding exists).
///
/// <c>dev_</c> is shape-checked only - BIO-DEV-001 is not a declared dependency of this story
/// (only BIO-IDN-001 and BIO-ARC-003 are), so a device id is a caller-supplied evaluation input,
/// never existence-gated against the device registry, the same "undeclared dependency stays
/// caller-supplied" discipline BIO-ACC-001 applied to card status/location/profile key.
/// </summary>
public static partial class CanonicalId
{
    public const string EventPrefix = "evt";

    // RFC 9562 UUIDv7, lowercase, dashed: version nibble pinned to 7, variant nibble to
    // 0b10 (8/9/a/b) - contracts/schemas/canonical-id.schema.json's pattern.
    [GeneratedRegex(@"^evt_[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$")]
    private static partial Regex EventIdPattern();

    [GeneratedRegex(@"^per_[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$")]
    private static partial Regex PersonIdPattern();

    [GeneratedRegex(@"^ten_[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$")]
    private static partial Regex TenantIdPattern();

    [GeneratedRegex(@"^dev_[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$")]
    private static partial Regex DeviceIdPattern();

    public static bool IsEventId(string? value) => value is not null && EventIdPattern().IsMatch(value);

    public static bool IsPersonId(string? value) => value is not null && PersonIdPattern().IsMatch(value);

    public static bool IsTenantId(string? value) => value is not null && TenantIdPattern().IsMatch(value);

    public static bool IsDeviceId(string? value) => value is not null && DeviceIdPattern().IsMatch(value);

    /// <summary>Mint a fresh <c>evt_</c> identifier for a newly captured attendance event, an audit event, or a minted correlation id.</summary>
    public static string NewEventId() => $"{EventPrefix}_{Guid.CreateVersion7():D}";
}
