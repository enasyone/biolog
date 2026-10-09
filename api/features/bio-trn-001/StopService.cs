using Biolog.Features.Transport.Api;
using Biolog.Features.Transport.Audit;
using Biolog.Features.Transport.Domain;
using Biolog.Features.Transport.Identifiers;
using Biolog.Features.Transport.Tenancy;

namespace Biolog.Features.Transport;

/// <summary>AC01: stop create/update/retire. Same audit-atomicity pattern and open scope as <see cref="RouteService"/>/<see cref="VehicleService"/>.</summary>
internal sealed class StopService(
    ICurrentTenantProvider tenantProvider,
    IStopRepository repository,
    ITransportAuditSink auditSink,
    StopLockRegistry lockRegistry)
{
    public async Task<StopRecord> CreateAsync(CreateStopRequest request, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        var name = ValidateName(request.Name);
        var campusId = ValidateCampusId(request.CampusId);

        var now = UtcInstant.Now();
        var record = StopRecord.Create(tenantId, campusId, name, request.Description?.Trim(), actor.Id, now);

        using var _ = await lockRegistry.AcquireAsync(tenantId, record.Id, cancellationToken);
        await AuditAsync(tenantId, actor, "transport.stop_created", record.Id, "stop_created", correlationId, cancellationToken);
        return await repository.CreateAsync(record, CancellationToken.None);
    }

    public async Task<StopRecord> GetAsync(string stopId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        return await repository.GetAsync(tenantId, stopId, cancellationToken) ?? throw TransportError.StopNotFound(stopId);
    }

    public Task<IReadOnlyList<StopRecord>> ListAsync(CancellationToken cancellationToken = default) =>
        repository.ListByTenantAsync(RequireTenant(), cancellationToken);

    public async Task<StopRecord> UpdateDetailsAsync(string stopId, UpdateStopRequest request, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        var name = ValidateName(request.Name);
        var campusId = ValidateCampusId(request.CampusId);

        using var _ = await lockRegistry.AcquireAsync(tenantId, stopId, cancellationToken);
        var current = await repository.GetAsync(tenantId, stopId, cancellationToken) ?? throw TransportError.StopNotFound(stopId);
        RequireVersion(stopId, request.Version, current.Version);

        var updated = current.UpdateDetails(campusId, name, request.Description?.Trim(), actor.Id, UtcInstant.Now());
        await AuditAsync(tenantId, actor, "transport.stop_updated", stopId, "stop_updated", correlationId, cancellationToken);
        return await repository.ReplaceAsync(current, updated, CancellationToken.None);
    }

    public async Task<StopRecord> RetireAsync(string stopId, VersionedTransportRequest request, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        using var _ = await lockRegistry.AcquireAsync(tenantId, stopId, cancellationToken);
        var current = await repository.GetAsync(tenantId, stopId, cancellationToken) ?? throw TransportError.StopNotFound(stopId);
        RequireVersion(stopId, request.Version, current.Version);

        if (current.Status == StopStatus.Retired)
        {
            throw TransportError.InvalidStatusTransition(stopId, current.Status.ToWireString(), StopStatus.Retired.ToWireString());
        }

        var updated = current.Retire(actor.Id, UtcInstant.Now());
        await AuditAsync(tenantId, actor, "transport.stop_retired", stopId, "stop_retired", correlationId, cancellationToken);
        return await repository.ReplaceAsync(current, updated, CancellationToken.None);
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
            tenantId, actor, action, new AuditTarget("stop", targetId), AuditOutcome.Allowed, reasonCode, correlationId, null, new Dictionary<string, (object?, string)>());

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
