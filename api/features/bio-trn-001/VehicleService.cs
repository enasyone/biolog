using Biolog.Features.Transport.Api;
using Biolog.Features.Transport.Audit;
using Biolog.Features.Transport.Domain;
using Biolog.Features.Transport.Identifiers;
using Biolog.Features.Transport.Tenancy;

namespace Biolog.Features.Transport;

/// <summary>
/// AC01/AC03: vehicle create/update/out-of-service/reactivate/retire, including AC03's
/// device-linkage validation via <see cref="IDeviceExistenceGateway"/>. Same audit-atomicity
/// pattern as <see cref="RouteService"/>; same open-to-any-asserted-actor scope (see
/// <see cref="IPermissionGateway"/>'s header).
/// </summary>
internal sealed class VehicleService(
    ICurrentTenantProvider tenantProvider,
    IVehicleRepository repository,
    ITransportAuditSink auditSink,
    VehicleLockRegistry lockRegistry,
    IDeviceExistenceGateway deviceGateway)
{
    public async Task<VehicleRecord> CreateAsync(CreateVehicleRequest request, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        var name = ValidateName(request.Name);
        var campusId = ValidateCampusId(request.CampusId);
        var capacity = ValidateCapacity(request.Capacity);
        await ValidateDeviceIdAsync(request.DeviceId, cancellationToken);

        var now = UtcInstant.Now();
        var record = VehicleRecord.Create(tenantId, campusId, name, capacity, request.DeviceId, actor.Id, now);

        using var _ = await lockRegistry.AcquireAsync(tenantId, record.Id, cancellationToken);
        await AuditAsync(tenantId, actor, "transport.vehicle_created", record.Id, "vehicle_created", correlationId, cancellationToken);
        return await repository.CreateAsync(record, CancellationToken.None);
    }

    public async Task<VehicleRecord> GetAsync(string vehicleId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        return await repository.GetAsync(tenantId, vehicleId, cancellationToken) ?? throw TransportError.VehicleNotFound(vehicleId);
    }

    public Task<IReadOnlyList<VehicleRecord>> ListAsync(CancellationToken cancellationToken = default) =>
        repository.ListByTenantAsync(RequireTenant(), cancellationToken);

    public async Task<VehicleRecord> UpdateDetailsAsync(string vehicleId, UpdateVehicleRequest request, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        var name = ValidateName(request.Name);
        var campusId = ValidateCampusId(request.CampusId);
        var capacity = ValidateCapacity(request.Capacity);
        await ValidateDeviceIdAsync(request.DeviceId, cancellationToken);

        using var _ = await lockRegistry.AcquireAsync(tenantId, vehicleId, cancellationToken);
        var current = await repository.GetAsync(tenantId, vehicleId, cancellationToken) ?? throw TransportError.VehicleNotFound(vehicleId);
        RequireVersion(vehicleId, request.Version, current.Version);

        var updated = current.UpdateDetails(campusId, name, capacity, request.DeviceId, actor.Id, UtcInstant.Now());
        await AuditAsync(tenantId, actor, "transport.vehicle_updated", vehicleId, "vehicle_updated", correlationId, cancellationToken);
        return await repository.ReplaceAsync(current, updated, CancellationToken.None);
    }

    public async Task<VehicleRecord> SetOutOfServiceAsync(string vehicleId, VersionedTransportRequest request, AuditActor actor, string correlationId, CancellationToken cancellationToken = default) =>
        await TransitionAsync(vehicleId, request, actor, correlationId, VehicleStatus.OutOfService, r => r.SetOutOfService(actor.Id, UtcInstant.Now()), "transport.vehicle_out_of_service", "vehicle_out_of_service", cancellationToken);

    public async Task<VehicleRecord> ReactivateAsync(string vehicleId, VersionedTransportRequest request, AuditActor actor, string correlationId, CancellationToken cancellationToken = default) =>
        await TransitionAsync(vehicleId, request, actor, correlationId, VehicleStatus.Active, r => r.Reactivate(actor.Id, UtcInstant.Now()), "transport.vehicle_reactivated", "vehicle_reactivated", cancellationToken);

    public async Task<VehicleRecord> RetireAsync(string vehicleId, VersionedTransportRequest request, AuditActor actor, string correlationId, CancellationToken cancellationToken = default) =>
        await TransitionAsync(vehicleId, request, actor, correlationId, VehicleStatus.Retired, r => r.Retire(actor.Id, UtcInstant.Now()), "transport.vehicle_retired", "vehicle_retired", cancellationToken);

    private async Task<VehicleRecord> TransitionAsync(
        string vehicleId, VersionedTransportRequest request, AuditActor actor, string correlationId,
        VehicleStatus target, Func<VehicleRecord, VehicleRecord> transition, string action, string reasonCode, CancellationToken cancellationToken)
    {
        var tenantId = RequireTenant();
        using var _ = await lockRegistry.AcquireAsync(tenantId, vehicleId, cancellationToken);
        var current = await repository.GetAsync(tenantId, vehicleId, cancellationToken) ?? throw TransportError.VehicleNotFound(vehicleId);
        RequireVersion(vehicleId, request.Version, current.Version);

        if (!TransportTransitions.IsAllowedVehicleTransition(current.Status, target))
        {
            throw TransportError.InvalidStatusTransition(vehicleId, current.Status.ToWireString(), target.ToWireString());
        }

        var updated = transition(current);
        await AuditAsync(tenantId, actor, action, vehicleId, reasonCode, correlationId, cancellationToken);
        return await repository.ReplaceAsync(current, updated, CancellationToken.None);
    }

    /// <summary>AC03 - existence only, never status-gated (see <see cref="VehicleRecord"/>'s own header).</summary>
    private async Task ValidateDeviceIdAsync(string? deviceId, CancellationToken cancellationToken)
    {
        if (deviceId is null)
        {
            return;
        }

        if (!Identifiers.CanonicalId.IsDeviceId(deviceId))
        {
            throw TransportError.Validation([new Api.ValidationErrorItem("device_id", "invalid_format", "device_id must be a canonical dev_ identifier when present")]);
        }

        if (!await deviceGateway.ExistsAsync(deviceId, cancellationToken))
        {
            throw TransportError.DeviceNotFound(deviceId);
        }
    }

    private static string ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw TransportError.Validation([new Api.ValidationErrorItem("name", "required", "name is required")]);
        }

        var trimmed = name.Trim();
        if (trimmed.Length > 200)
        {
            throw TransportError.Validation([new Api.ValidationErrorItem("name", "out_of_range", "name must be at most 200 characters")]);
        }

        return trimmed;
    }

    private static string? ValidateCampusId(string? campusId)
    {
        if (campusId is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(campusId))
        {
            throw TransportError.Validation([new Api.ValidationErrorItem("campus_id", "invalid_format", "campus_id must not be blank when present")]);
        }

        return campusId.Trim();
    }

    private static int? ValidateCapacity(int? capacity)
    {
        if (capacity is int value && value <= 0)
        {
            throw TransportError.Validation([new Api.ValidationErrorItem("capacity", "out_of_range", "capacity must be a positive integer when present")]);
        }

        return capacity;
    }

    private static void RequireVersion(string id, int? requested, int actual)
    {
        if (requested is not int expected)
        {
            throw TransportError.Validation([new Api.ValidationErrorItem("version", "required", "version is required so a stale write can be detected")]);
        }

        if (expected != actual)
        {
            throw TransportError.ConcurrencyConflict(id, expected, actual);
        }
    }

    private async Task AuditAsync(string tenantId, AuditActor actor, string action, string targetId, string reasonCode, string correlationId, CancellationToken cancellationToken)
    {
        var auditEvent = TransportAuditEventBuilder.Build(
            tenantId, actor, action, new AuditTarget("vehicle", targetId), AuditOutcome.Allowed, reasonCode, correlationId, null, new Dictionary<string, (object?, string)>());

        try
        {
            await auditSink.WriteAsync(auditEvent, cancellationToken);
        }
        catch
        {
            if (!await auditSink.ExistsAsync(auditEvent.EventId, CancellationToken.None))
            {
                throw TransportError.AuditUnavailable();
            }
        }
    }

    private string RequireTenant() => tenantProvider.TenantId ?? throw TransportError.TenantContextMissing();
}
