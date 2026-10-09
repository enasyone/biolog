using System.Text.RegularExpressions;

namespace Biolog.Features.AttendanceSessions.Identifiers;

/// <summary>
/// Canonical BIOLOG identifiers (ADR-0001 Decision 1), scoped to the REGISTERED types this
/// feature touches: <c>per_</c>/<c>ten_</c>/<c>evt_</c> (defense-in-depth shape validation only).
/// A local, schema-conformant helper - not the shared contract, the same "hand-ported pattern, not
/// a shared library" precedent every prior <c>.NET</c> feature in this codebase follows.
///
/// <see cref="AttendanceAdjustment"/> mints a registered <c>evt_</c> id (it genuinely is an
/// auditable act, the same choice BIO-ATT-001's own <c>AttendanceEvent.EventId</c> and
/// BIO-TRN-002's own <c>TransportEvent.EventId</c> make). <c>Session</c> and
/// <c>SessionAttendancePolicy</c> mint a plain, undecorated UUIDv7 (<see cref="NewLocalId"/>) - no
/// registered identifier-registry type exists for either, the same BIO-ACC-001/BIO-VIS-001/
/// BIO-TRN-001 no-canonical-prefix precedent.
/// </summary>
public static partial class CanonicalId
{
    public const string EventPrefix = "evt";

    [GeneratedRegex(@"^evt_[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$")]
    private static partial Regex EventIdPattern();

    [GeneratedRegex(@"^per_[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$")]
    private static partial Regex PersonIdPattern();

    [GeneratedRegex(@"^ten_[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$")]
    private static partial Regex TenantIdPattern();

    public static bool IsEventId(string? value) => value is not null && EventIdPattern().IsMatch(value);

    public static bool IsPersonId(string? value) => value is not null && PersonIdPattern().IsMatch(value);

    public static bool IsTenantId(string? value) => value is not null && TenantIdPattern().IsMatch(value);

    /// <summary>Mint a fresh <c>evt_</c> identifier for an <see cref="AttendanceAdjustment"/> or a minted correlation id.</summary>
    public static string NewEventId() => $"{EventPrefix}_{Guid.CreateVersion7():D}";

    /// <summary>Mint a fresh, non-canonical-registry identity for a record this feature owns that has no registered type of its own (<c>Session</c>, <c>SessionAttendancePolicy</c>).</summary>
    public static string NewLocalId() => Guid.CreateVersion7().ToString();
}
