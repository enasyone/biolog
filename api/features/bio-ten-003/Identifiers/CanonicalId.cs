using System.Text.RegularExpressions;

namespace Biolog.Features.TenantFeatureConfig.Identifiers;

/// <summary>
/// Canonical BIOLOG identifiers (ADR-0001 Decision 1), scoped to what this feature needs:
/// <c>ten_</c> (validating an inbound tenant id's shape - this feature never mints one, that
/// is BIO-TEN-001's own job) and <c>evt_</c> (audit/correlation events, ADR-0003 Decision 1).
/// A local, schema-conformant helper, copied and scoped down from
/// <c>api/features/bio-ten-001/Identifiers/CanonicalId.cs</c> per this codebase's established
/// "each feature keeps its own copy" convention (contracts/README.md: no generated .NET
/// binding exists yet for any feature to share instead) - not the shared contract itself.
/// </summary>
public static partial class CanonicalId
{
    public const string TenantPrefix = "ten";
    public const string EventPrefix = "evt";

    [GeneratedRegex(@"^ten_[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$")]
    private static partial Regex TenantIdPattern();

    [GeneratedRegex(@"^evt_[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$")]
    private static partial Regex EventIdPattern();

    public static bool IsTenantId(string? value) => value is not null && TenantIdPattern().IsMatch(value);

    public static bool IsEventId(string? value) => value is not null && EventIdPattern().IsMatch(value);

    /// <summary>Mint a fresh <c>evt_</c> identifier for an audit event or a minted correlation id.</summary>
    public static string NewEventId() => $"{EventPrefix}_{Guid.CreateVersion7():D}";
}
