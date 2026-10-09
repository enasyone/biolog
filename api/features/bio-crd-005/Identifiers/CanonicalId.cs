using System.Text.RegularExpressions;

namespace Biolog.Features.CardIssuance.Identifiers;

/// <summary>
/// Canonical BIOLOG identifiers (ADR-0001 Decision 1), scoped to the three types this feature
/// touches: <c>per_</c> (the person a card is issued to - shape-validated only, existence
/// checked via <see cref="Domain.IPersonExistenceGateway"/>, never minted here), <c>ten_</c>
/// (defence-in-depth shape validation for the resolved tenant) and <c>evt_</c> (audit/
/// correlation events, minted here). This is a local, schema-conformant helper - not the
/// shared contract; no generated .NET binding exists yet. This project registers no new
/// identifier type of its own - <see cref="Domain.CardIssue"/> deliberately has no canonical
/// id of its own kind (it is keyed by a natural/composite key against <c>PersonId</c> and
/// <c>CardClass</c> - see that type's header for why).
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

    /// <summary>Mint a fresh <c>evt_</c> identifier for an audit event or a minted correlation id.</summary>
    public static string NewEventId() => $"{EventPrefix}_{Guid.CreateVersion7():D}";
}
