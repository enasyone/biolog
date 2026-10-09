using System.Text.RegularExpressions;

namespace Biolog.Features.InstitutionalIdentifiers.Identifiers;

/// <summary>
/// Canonical BIOLOG identifiers (ADR-0001 Decision 1), scoped to the two types this feature
/// touches: <c>per_</c> (the person an identifier attaches to - shape-validated only here;
/// EXISTENCE is proven through <see cref="Domain.IPersonExistenceGateway"/>, never a
/// ProjectReference, see that interface's header) and <c>evt_</c> (audit/correlation events,
/// minted here). This is a local, schema-conformant helper - not the shared contract; no
/// generated .NET binding exists yet. This project registers no new identifier type of its
/// own - an institutional identifier is a business identifier (ADR-0001 Decision 1.6,
/// <c>contracts/schemas/identifier-registry.json</c>'s <c>business_identifiers</c> block names
/// "institutional identifier" and "matriculation number" verbatim), never a canonical id.
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
