using System.Text.RegularExpressions;

namespace Biolog.Features.Rbac.Identifiers;

/// <summary>
/// Canonical BIOLOG identifiers (ADR-0001 Decision 1), scoped to the two types this feature
/// touches: <c>per_</c> (the person a role/grant/approval applies to - shape-validated only
/// here; EXISTENCE is proven through <see cref="Domain.IPersonExistenceGateway"/>, never a
/// ProjectReference) and <c>evt_</c> (audit/correlation events, minted here). This is a local,
/// schema-conformant helper - not the shared contract; no generated .NET binding exists yet.
/// This project registers no new identifier type of its own: <c>RoleType</c> is a closed enum
/// (AC01's fixed baseline catalogue), and every other aggregate here
/// (<c>PersonRoleAssignment</c>, <c>RolePermissionGrant</c>, <c>SensitiveActionRule</c>,
/// <c>ApprovalRequest</c>) uses a natural/composite key - the same "prefer a natural key over
/// minting a new canonical type" discipline every sibling story in this epic already applies
/// (see each aggregate's own header for its specific key).
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
