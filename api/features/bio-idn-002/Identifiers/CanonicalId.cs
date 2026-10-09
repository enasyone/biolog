using System.Text.RegularExpressions;

namespace Biolog.Features.PersonProfileRegistry.Identifiers;

/// <summary>
/// Canonical BIOLOG identifiers (ADR-0001 Decision 1), scoped to the three types this feature
/// touches: <c>per_</c> (the person a profile attaches to - shape-validated only, never minted
/// or verified for existence here, see this project's csproj remarks), <c>ten_</c> (defense-
/// in-depth shape validation for the resolved tenant, mirroring BIO-IDN-001-QA-01's identical
/// use in <c>Person</c>'s own constructor) and <c>evt_</c> (audit/correlation events, minted
/// here). This is a local, schema-conformant helper - not the shared contract; no generated
/// .NET binding exists yet. This project registers no new identifier type of its own -
/// <see cref="Domain.PersonProfile"/> deliberately has no canonical id of its own kind (its
/// natural key is <c>(PersonId, ProfileType)</c> - see that type's header for why).
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
