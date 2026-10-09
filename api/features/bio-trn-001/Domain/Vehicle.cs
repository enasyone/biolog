using Biolog.Features.Transport.Identifiers;

namespace Biolog.Features.Transport.Domain;

/// <summary>
/// AC01 - a vehicle's own versioned lifecycle. AC03 - <see cref="DeviceId"/> is the vehicle/device
/// linkage: nullable, and when present existence-checked (only) via
/// <see cref="IDeviceExistenceGateway"/> against BIO-DEV-001's own device registry - NOT
/// status-gated. Pairing a vehicle with a not-yet-<c>Active</c> device is normal pre-deployment
/// inventory work, not an authorization decision (re-derived, not pattern-matched from
/// BIO-VIS-001's own gateway boundary reasoning - see docs/features/bio-trn-001/README.md "AC03:
/// vehicle/device linkage is existence-checked, not status-gated").
/// </summary>
public sealed record VehicleRecord
{
    public string Id { get; }
    public string TenantId { get; }

    public string? CampusId { get; init; }
    public required string Name { get; init; }
    public int? Capacity { get; init; }
    public string? DeviceId { get; init; }
    public required VehicleStatus Status { get; init; }
    public required int Version { get; init; }
    public required string CreatedAt { get; init; }
    public required string UpdatedAt { get; init; }
    public required string UpdatedBy { get; init; }

    internal VehicleRecord(string id, string tenantId)
    {
        if (!CanonicalId.IsTenantId(tenantId))
        {
            throw new ArgumentException($"'{tenantId}' is not a well-formed tenant id (ADR-0001)", nameof(tenantId));
        }

        Id = id;
        TenantId = tenantId;
    }

    internal static VehicleRecord Create(string tenantId, string? campusId, string name, int? capacity, string? deviceId, string actorId, string now) =>
        new(CanonicalId.NewLocalId(), tenantId)
        {
            CampusId = campusId,
            Name = name,
            Capacity = capacity,
            DeviceId = deviceId,
            Status = VehicleStatus.Active,
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now,
            UpdatedBy = actorId,
        };

    internal VehicleRecord UpdateDetails(string? campusId, string name, int? capacity, string? deviceId, string actorId, string now) =>
        this with { CampusId = campusId, Name = name, Capacity = capacity, DeviceId = deviceId, Version = Version + 1, UpdatedAt = now, UpdatedBy = actorId };

    internal VehicleRecord SetOutOfService(string actorId, string now) =>
        this with { Status = VehicleStatus.OutOfService, Version = Version + 1, UpdatedAt = now, UpdatedBy = actorId };

    internal VehicleRecord Reactivate(string actorId, string now) =>
        this with { Status = VehicleStatus.Active, Version = Version + 1, UpdatedAt = now, UpdatedBy = actorId };

    internal VehicleRecord Retire(string actorId, string now) =>
        this with { Status = VehicleStatus.Retired, Version = Version + 1, UpdatedAt = now, UpdatedBy = actorId };
}
