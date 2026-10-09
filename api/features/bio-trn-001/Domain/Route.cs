using Biolog.Features.Transport.Identifiers;

namespace Biolog.Features.Transport.Domain;

/// <summary>One stop on a <see cref="Route"/>'s own ordered list - a value, not an aggregate reference: <see cref="StopId"/> names a real, independently-versioned <see cref="Stop"/> (existence/active-checked by <see cref="RouteService"/> against <see cref="IStopRepository"/> at the time the route is created/updated), but the sequence/offset belong to the ROUTE's own shape, not the stop's.</summary>
public sealed record RouteStopEntry
{
    public required string StopId { get; init; }
    public required int SequenceOrder { get; init; }
    public int? EstimatedOffsetMinutes { get; init; }
}

/// <summary>
/// AC01 - a route's own versioned lifecycle (see <see cref="RouteStatus"/>). A mutable,
/// CAS-versioned aggregate (internal constructor, <c>get</c>-only <see cref="Id"/>/
/// <see cref="TenantId"/> even across <c>with</c>, internal mutation methods) - the same shape
/// BIO-VIS-001's own <c>VisitorRecord</c> and BIO-ATT-001's own <c>AttendanceEvent</c>-family
/// aggregates establish for this codebase.
///
/// <b>No canonical id prefix.</b> See <c>Identifiers/CanonicalId.cs</c>'s own header - <see cref="Id"/>
/// is a plain UUIDv7 string, deliberately not claiming conformance with the Kemi-owned identifier
/// registry (same BIO-ACC-001/BIO-VIS-001 precedent).
///
/// <b>AC04 - <see cref="CampusId"/> is a feature-local scoping field, not a registered entity.</b>
/// No Campus service exists anywhere in this repository and none is a declared dependency of this
/// story; it is shape-validated (non-empty when present) only. See
/// <c>PersonAssignmentService.CreateAsync</c> for the one real cross-field consistency rule this
/// feature enforces with it.
/// </summary>
public sealed record RouteRecord
{
    public string Id { get; }
    public string TenantId { get; }

    public string? CampusId { get; init; }
    public required string Name { get; init; }
    public required RouteStatus Status { get; init; }
    public required IReadOnlyList<RouteStopEntry> Stops { get; init; }
    public required int Version { get; init; }
    public required string CreatedAt { get; init; }
    public required string UpdatedAt { get; init; }
    public required string UpdatedBy { get; init; }

    internal RouteRecord(string id, string tenantId)
    {
        if (!CanonicalId.IsTenantId(tenantId))
        {
            throw new ArgumentException($"'{tenantId}' is not a well-formed tenant id (ADR-0001)", nameof(tenantId));
        }

        Id = id;
        TenantId = tenantId;
    }

    internal static RouteRecord Create(string tenantId, string? campusId, string name, IReadOnlyList<RouteStopEntry> stops, string actorId, string now) =>
        new(CanonicalId.NewLocalId(), tenantId)
        {
            CampusId = campusId,
            Name = name,
            Status = RouteStatus.Draft,
            Stops = stops,
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now,
            UpdatedBy = actorId,
        };

    internal RouteRecord UpdateDetails(string? campusId, string name, IReadOnlyList<RouteStopEntry> stops, string actorId, string now) =>
        this with { CampusId = campusId, Name = name, Stops = stops, Version = Version + 1, UpdatedAt = now, UpdatedBy = actorId };

    internal RouteRecord Activate(string actorId, string now) =>
        this with { Status = RouteStatus.Active, Version = Version + 1, UpdatedAt = now, UpdatedBy = actorId };

    internal RouteRecord Retire(string actorId, string now) =>
        this with { Status = RouteStatus.Retired, Version = Version + 1, UpdatedAt = now, UpdatedBy = actorId };
}
