using Biolog.Features.TransportEvents.Identifiers;

namespace Biolog.Features.TransportEvents.Domain;

/// <summary>
/// AC04's tenant-wide notification-trigger configuration. Mutable, CAS-versioned - mirrors
/// <c>AttendancePolicy</c>/<c>VisitorTenantConfig</c>'s own shape exactly. A tenant with no
/// explicit config still has a real, non-null effective default (<see cref="Default"/>) - the
/// same "never a null/missing config" discipline every sibling tenant-config type in this
/// codebase applies.
///
/// <b>Why <see cref="TransportNotificationConfigService.SetAsync"/> is NOT gated by
/// <c>IPermissionGateway</c>, unlike <c>AttendancePolicy.SetAsync</c>/<c>VisitorTenantConfig.SetAsync</c>.</b>
/// Re-derived (not defaulted either way): those two configs change ENFORCEMENT rules (what
/// counts as late/absent, what a visitor must complete before check-in) - a real interpretive
/// authority. This config only controls whether an internal, audit-adjacent
/// <see cref="TriggeredNotification"/> record is written when a condition is met; it grants no
/// differential access to anyone and gates no physical action. See
/// docs/features/bio-trn-002/README.md "AC04: why notification config stays open" for the full
/// re-derivation.
/// </summary>
public sealed record TransportNotificationConfig
{
    public required string TenantId { get; init; }
    public required bool NotifyOnMissingAlightException { get; init; }
    public required int Version { get; init; }
    public required string UpdatedAt { get; init; }
    public required string UpdatedBy { get; init; }

    /// <summary>The effective config for a tenant that has never called <c>SetAsync</c> - no notifications trigger until explicitly enabled. <see cref="Version"/> 0 is this sentinel's own marker (never a real stored version, matching <c>AttendancePolicy.Default</c>'s own convention).</summary>
    public static TransportNotificationConfig Default(string tenantId) => new()
    {
        TenantId = tenantId,
        NotifyOnMissingAlightException = false,
        Version = 0,
        UpdatedAt = UtcInstant.Render(DateTime.UnixEpoch),
        UpdatedBy = "system",
    };

    internal static TransportNotificationConfig Create(string tenantId, bool notifyOnMissingAlightException, string actorId, string now) => new()
    {
        TenantId = tenantId,
        NotifyOnMissingAlightException = notifyOnMissingAlightException,
        Version = 1,
        UpdatedAt = now,
        UpdatedBy = actorId,
    };

    internal TransportNotificationConfig Update(bool notifyOnMissingAlightException, string actorId, string now) =>
        this with { NotifyOnMissingAlightException = notifyOnMissingAlightException, Version = Version + 1, UpdatedAt = now, UpdatedBy = actorId };
}
