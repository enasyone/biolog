using System.Text.RegularExpressions;

namespace Biolog.Features.WorkforceAttendance.Identifiers;

/// <summary>
/// Canonical BIOLOG identifiers (ADR-0001 Decision 1), scoped to what this feature needs:
/// <c>evt_</c> (minted for every new WorkforceShift and WorkforceAttendanceAdjustment record -
/// the same "any newly-created auditable record mints evt_" convention
/// BIO-ATT-002's own AttendanceAdjustment and BIO-ATT-001's own AttendanceEvent both use) and
/// <c>per_</c>/<c>ten_</c> (defense-in-depth shape validation only). A local, schema-conformant
/// helper, copied and scoped down from <c>api/features/bio-att-001/Identifiers/CanonicalId.cs</c>
/// per this codebase's established "each feature keeps its own copy" convention.
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

    /// <summary>Mint a fresh <c>evt_</c> identifier for a new WorkforceShift, WorkforceAttendanceAdjustment, audit event, or minted correlation id.</summary>
    public static string NewEventId() => $"{EventPrefix}_{Guid.CreateVersion7():D}";
}
