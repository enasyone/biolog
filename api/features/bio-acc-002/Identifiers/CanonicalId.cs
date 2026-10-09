using System.Text.RegularExpressions;

namespace Biolog.Features.AccessDecisions.Identifiers;

/// <summary>
/// Canonical BIOLOG identifiers (ADR-0001 Decision 1), scoped to the REGISTERED types this
/// feature touches: <c>per_</c>/<c>ten_</c>/<c>evt_</c> (defense-in-depth shape validation only).
/// A local, schema-conformant helper - not the shared contract.
///
/// <b>AccessDecisionEvent's own EventId IS the registered <c>evt_</c> type</b> (mirrors
/// BIO-ATT-001's <c>AttendanceEvent.EventId</c>/BIO-TRN-002's <c>TransportEvent.EventId</c> choice
/// exactly - a computed access decision genuinely is a domain event). No other identifier is
/// minted by this feature.
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

    /// <summary>Mint a fresh <c>evt_</c> identifier (a registered canonical type) for a computed AccessDecisionEvent, an audit event, or a minted correlation id.</summary>
    public static string NewEventId() => $"{EventPrefix}_{Guid.CreateVersion7():D}";
}
