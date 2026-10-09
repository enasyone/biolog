using Biolog.Features.Visitors.Identifiers;

namespace Biolog.Features.Visitors.Domain;

/// <summary>
/// AC03 "configurable tenant feature controls" - one tenant config (natural key
/// <c>TenantId</c> alone, mirroring BIO-ATT-001's own <c>AttendancePolicy</c>). A mutable,
/// CAS-versioned tenant aggregate - a live configuration setting a tenant administrator may
/// revise, not an immutable fact.
///
/// <see cref="VisitorRecordService.CheckInAsync"/> is the only reader of the four
/// <c>Require*</c> fields - they gate CHECK-IN, not preregistration (see
/// <see cref="VisitorRecord"/>'s own header for why). <see cref="AllowTemporaryCredential"/>
/// gates <see cref="VisitorRecordService.IssueTemporaryCredentialAsync"/>.
/// <see cref="DefaultVisitDurationHours"/>/<see cref="MaxVisitDurationHours"/> govern AC04's
/// expiry: every preregistration's <c>ExpiresAt</c> is computed from these unless the caller
/// supplies an explicit value no later than the max.
/// </summary>
public sealed class VisitorTenantConfig
{
    public string TenantId { get; }
    public bool RequireVehiclePlate { get; }
    public bool RequireNda { get; }
    public bool RequireSafetyBriefing { get; }
    public bool RequireWatchlistScreening { get; }
    public bool AllowTemporaryCredential { get; }
    public int DefaultVisitDurationHours { get; }
    public int MaxVisitDurationHours { get; }
    public int Version { get; }
    public string UpdatedAt { get; }
    public string UpdatedBy { get; }

    private VisitorTenantConfig(
        string tenantId, bool requireVehiclePlate, bool requireNda, bool requireSafetyBriefing, bool requireWatchlistScreening,
        bool allowTemporaryCredential, int defaultVisitDurationHours, int maxVisitDurationHours, int version, string updatedAt, string updatedBy)
    {
        TenantId = tenantId;
        RequireVehiclePlate = requireVehiclePlate;
        RequireNda = requireNda;
        RequireSafetyBriefing = requireSafetyBriefing;
        RequireWatchlistScreening = requireWatchlistScreening;
        AllowTemporaryCredential = allowTemporaryCredential;
        DefaultVisitDurationHours = defaultVisitDurationHours;
        MaxVisitDurationHours = maxVisitDurationHours;
        Version = version;
        UpdatedAt = updatedAt;
        UpdatedBy = updatedBy;
    }

    /// <summary>The config every tenant has implicitly until an administrator sets one explicitly - nothing required, no temporary credential offered, an 8h default/24h max visit window. Version 0 so the first real <see cref="Update"/> is distinguishable from a genuine prior version.</summary>
    public static VisitorTenantConfig Default(string tenantId) => new(
        tenantId, requireVehiclePlate: false, requireNda: false, requireSafetyBriefing: false, requireWatchlistScreening: false,
        allowTemporaryCredential: false, defaultVisitDurationHours: 8, maxVisitDurationHours: 24, version: 0,
        updatedAt: UtcInstant.Render(DateTime.UnixEpoch), updatedBy: "system");

    public static VisitorTenantConfig Create(
        string tenantId, bool requireVehiclePlate, bool requireNda, bool requireSafetyBriefing, bool requireWatchlistScreening,
        bool allowTemporaryCredential, int defaultVisitDurationHours, int maxVisitDurationHours, string actorId, string now)
    {
        ValidateFields(defaultVisitDurationHours, maxVisitDurationHours);
        return new VisitorTenantConfig(
            tenantId, requireVehiclePlate, requireNda, requireSafetyBriefing, requireWatchlistScreening,
            allowTemporaryCredential, defaultVisitDurationHours, maxVisitDurationHours, version: 1, updatedAt: now, updatedBy: actorId);
    }

    /// <summary>Returns a NEW instance (never mutates this one) one version ahead - the caller (<see cref="VisitorConfigService"/>) is responsible for the compare-and-set write against <see cref="Version"/>.</summary>
    public VisitorTenantConfig Update(
        bool requireVehiclePlate, bool requireNda, bool requireSafetyBriefing, bool requireWatchlistScreening,
        bool allowTemporaryCredential, int defaultVisitDurationHours, int maxVisitDurationHours, string actorId, string now)
    {
        ValidateFields(defaultVisitDurationHours, maxVisitDurationHours);
        return new VisitorTenantConfig(
            TenantId, requireVehiclePlate, requireNda, requireSafetyBriefing, requireWatchlistScreening,
            allowTemporaryCredential, defaultVisitDurationHours, maxVisitDurationHours, Version + 1, now, actorId);
    }

    private static void ValidateFields(int defaultVisitDurationHours, int maxVisitDurationHours)
    {
        if (defaultVisitDurationHours < 1 || defaultVisitDurationHours > 24 * 30)
        {
            throw VisitorError.Validation([new Api.ValidationErrorItem("default_visit_duration_hours", "out_of_range", "default_visit_duration_hours must be between 1 and 720")]);
        }

        if (maxVisitDurationHours < defaultVisitDurationHours || maxVisitDurationHours > 24 * 30)
        {
            throw VisitorError.Validation([new Api.ValidationErrorItem("max_visit_duration_hours", "out_of_range", "max_visit_duration_hours must be at least default_visit_duration_hours and at most 720")]);
        }
    }
}
