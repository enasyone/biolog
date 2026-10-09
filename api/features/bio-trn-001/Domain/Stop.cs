using Biolog.Features.Transport.Identifiers;

namespace Biolog.Features.Transport.Domain;

/// <summary>
/// AC01 - a stop's own versioned lifecycle, independently reusable across multiple
/// <see cref="RouteRecord"/>s (a <see cref="RouteStopEntry"/> references a stop by id; retiring a
/// stop does not retroactively rewrite any route's own historical stop list). AC04 -
/// <see cref="CampusId"/> is the same feature-local, non-registered scoping field as
/// <see cref="RouteRecord.CampusId"/>/<see cref="VehicleRecord.CampusId"/> - see those types' own
/// headers.
/// </summary>
public sealed record StopRecord
{
    public string Id { get; }
    public string TenantId { get; }

    public string? CampusId { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required StopStatus Status { get; init; }
    public required int Version { get; init; }
    public required string CreatedAt { get; init; }
    public required string UpdatedAt { get; init; }
    public required string UpdatedBy { get; init; }

    internal StopRecord(string id, string tenantId)
    {
        if (!CanonicalId.IsTenantId(tenantId))
        {
            throw new ArgumentException($"'{tenantId}' is not a well-formed tenant id (ADR-0001)", nameof(tenantId));
        }

        Id = id;
        TenantId = tenantId;
    }

    internal static StopRecord Create(string tenantId, string? campusId, string name, string? description, string actorId, string now) =>
        new(CanonicalId.NewLocalId(), tenantId)
        {
            CampusId = campusId,
            Name = name,
            Description = description,
            Status = StopStatus.Active,
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now,
            UpdatedBy = actorId,
        };

    internal StopRecord UpdateDetails(string? campusId, string name, string? description, string actorId, string now) =>
        this with { CampusId = campusId, Name = name, Description = description, Version = Version + 1, UpdatedAt = now, UpdatedBy = actorId };

    internal StopRecord Retire(string actorId, string now) =>
        this with { Status = StopStatus.Retired, Version = Version + 1, UpdatedAt = now, UpdatedBy = actorId };
}
