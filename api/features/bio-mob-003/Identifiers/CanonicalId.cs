using System.Text.RegularExpressions;

namespace Biolog.Features.OfflineSync.Identifiers;

/// <summary>
/// Canonical BIOLOG identifiers (ADR-0001 Decision 1). This feature's own local,
/// schema-conformant mirror - not the shared contract; no generated .NET binding exists yet,
/// the same disclosed limitation every sibling feature project's own copy carries.
/// <see cref="IsAggregateId"/> matches every prefix <c>canonical-id.schema.json</c> allows
/// (<c>offline-mutation.schema.json#/properties/aggregate_id</c> is generic - this queue is
/// infrastructure for whatever domain aggregate a future story attaches to it, not scoped to
/// one prefix the way a sibling feature's own aggregate id is).
/// </summary>
public static partial class CanonicalId
{
    public const string EventPrefix = "evt";

    [GeneratedRegex(@"^dev_[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$")]
    private static partial Regex DeviceIdPattern();

    [GeneratedRegex(@"^ten_[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$")]
    private static partial Regex TenantIdPattern();

    [GeneratedRegex(@"^evt_[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$")]
    private static partial Regex EventIdPattern();

    [GeneratedRegex(@"^(ten|per|crd|dev|dcs|evt)_[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$")]
    private static partial Regex AggregateIdPattern();

    public static bool IsDeviceId(string? value) => value is not null && DeviceIdPattern().IsMatch(value);

    public static bool IsTenantId(string? value) => value is not null && TenantIdPattern().IsMatch(value);

    public static bool IsEventId(string? value) => value is not null && EventIdPattern().IsMatch(value);

    /// <summary>Matches `offline-mutation.schema.json#/properties/mutation_id`, which `$ref`s the generic `canonical-id.schema.json` - a mobile client mints it with the `evt_` prefix (see `mobile/lib/core/offline/offline_mutation.dart`), but this server validates the field per its own actual schema, not the one client's own choice of prefix.</summary>
    public static bool IsMutationId(string? value) => value is not null && AggregateIdPattern().IsMatch(value);

    public static bool IsAggregateId(string? value) => value is not null && AggregateIdPattern().IsMatch(value);

    /// <summary>Mint a fresh <c>evt_</c> identifier for an audit event or a minted correlation id.</summary>
    public static string NewEventId() => $"{EventPrefix}_{Guid.CreateVersion7():D}";
}
