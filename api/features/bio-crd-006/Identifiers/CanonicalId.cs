using System.Text.RegularExpressions;

namespace Biolog.Features.CardLifecycle.Identifiers;

/// <summary>
/// Canonical BIOLOG identifiers (ADR-0001 Decision 1), scoped to the types this feature
/// touches: <c>per_</c> (shape-validated only - existence/ownership is delegated to
/// <see cref="Domain.ICardIssueGateway"/>, never minted here), <c>ten_</c> (defence-in-depth
/// shape validation for the resolved tenant), <c>evt_</c> (audit/correlation events, minted
/// here) and <c>dev_</c> (TD-CRD-006-DENY-LIST-DEVICE-AUTH: shape-validated only, existence/
/// status is delegated to <see cref="Domain.IDeviceExistenceGateway"/>, mirrors BIO-PHO-002's
/// own <c>CanonicalId.IsDeviceId</c> exactly). This project registers no new identifier type of
/// its own - a card report/replacement lifecycle record reuses BIO-CRD-005's own natural
/// composite key (TenantId, PersonId, CardClass, SequenceNumber), never a canonical id of its
/// own kind.
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

    [GeneratedRegex(@"^dev_[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$")]
    private static partial Regex DeviceIdPattern();

    public static bool IsPersonId(string? value) => value is not null && PersonIdPattern().IsMatch(value);

    public static bool IsTenantId(string? value) => value is not null && TenantIdPattern().IsMatch(value);

    public static bool IsEventId(string? value) => value is not null && EventIdPattern().IsMatch(value);

    public static bool IsDeviceId(string? value) => value is not null && DeviceIdPattern().IsMatch(value);

    /// <summary>Mint a fresh <c>evt_</c> identifier for an audit event or a minted correlation id.</summary>
    public static string NewEventId() => $"{EventPrefix}_{Guid.CreateVersion7():D}";
}
