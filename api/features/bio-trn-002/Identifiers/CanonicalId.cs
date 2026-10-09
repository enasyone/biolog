using System.Text.RegularExpressions;

namespace Biolog.Features.TransportEvents.Identifiers;

/// <summary>
/// Canonical BIOLOG identifiers (ADR-0001 Decision 1), scoped to the REGISTERED types this
/// feature touches: <c>per_</c>/<c>ten_</c>/<c>evt_</c> (defense-in-depth shape validation only).
/// A local, schema-conformant helper - not the shared contract.
///
/// <b>TransportEvent's own <see cref="EventId"/> IS the registered <c>evt_</c> type</b> (mirrors
/// BIO-ATT-001's own <c>AttendanceEvent.EventId</c> choice exactly - a captured board/alight fact
/// genuinely is a domain event). <see cref="TriggeredNotification"/>'s own id is a plain,
/// undecorated UUIDv7 (<see cref="NewLocalId"/>) - the same BIO-ACC-001/BIO-VIS-001/BIO-TRN-001
/// precedent for a record this story mints that has no registered type of its own.
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

    /// <summary>Mint a fresh <c>evt_</c> identifier (a registered canonical type) for a captured TransportEvent, an audit event, or a minted correlation id.</summary>
    public static string NewEventId() => $"{EventPrefix}_{Guid.CreateVersion7():D}";

    /// <summary>Mint a fresh, non-canonical-registry identity for a record this feature owns that has no registered type of its own (e.g. a <see cref="TriggeredNotification"/>).</summary>
    public static string NewLocalId() => Guid.CreateVersion7().ToString();
}
