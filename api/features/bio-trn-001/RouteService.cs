using Biolog.Features.Transport.Api;
using Biolog.Features.Transport.Audit;
using Biolog.Features.Transport.Domain;
using Biolog.Features.Transport.Identifiers;
using Biolog.Features.Transport.Tenancy;

namespace Biolog.Features.Transport;

/// <summary>
/// AC01: route create/update/activate/retire - the route half of this feature's versioned
/// registry. Implements ADR-0003 Decision 7's audit-atomicity pattern (per-record lock,
/// audit-before-commit, <see cref="ITransportAuditSink.ExistsAsync"/> verification on an
/// ambiguous audit-write throw, <see cref="CancellationToken.None"/> post-confirm).
///
/// <b>Open to any asserted actor - audit-attribution only, no permission/provenance gate</b> (see
/// <see cref="IPermissionGateway"/>'s own header for the full re-derivation). A route's own
/// name/campus/stop-sequence is registry/inventory content, not the one authorization decision
/// this feature makes (that is <see cref="PersonAssignmentService"/>'s <c>CreateAsync</c>/
/// <c>EndAsync</c>).
/// </summary>
internal sealed class RouteService(
    ICurrentTenantProvider tenantProvider,
    IRouteRepository repository,
    IStopRepository stopRepository,
    ITransportAuditSink auditSink,
    RouteLockRegistry lockRegistry)
{
    public async Task<RouteRecord> CreateAsync(CreateRouteRequest request, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        var name = ValidateName(request.Name);
        var campusId = ValidateCampusId(request.CampusId);
        var stops = await ValidateStopsAsync(tenantId, request.Stops, cancellationToken);

        var now = UtcInstant.Now();
        var record = RouteRecord.Create(tenantId, campusId, name, stops, actor.Id, now);

        using var _ = await lockRegistry.AcquireAsync(tenantId, record.Id, cancellationToken);
        await AuditAsync(tenantId, actor, "transport.route_created", record.Id, "route_created", correlationId, cancellationToken);
        return await repository.CreateAsync(record, CancellationToken.None);
    }

    public async Task<RouteRecord> GetAsync(string routeId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        return await repository.GetAsync(tenantId, routeId, cancellationToken) ?? throw TransportError.RouteNotFound(routeId);
    }

    public Task<IReadOnlyList<RouteRecord>> ListAsync(CancellationToken cancellationToken = default) =>
        repository.ListByTenantAsync(RequireTenant(), cancellationToken);

    public async Task<RouteRecord> UpdateDetailsAsync(string routeId, UpdateRouteRequest request, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        var name = ValidateName(request.Name);
        var campusId = ValidateCampusId(request.CampusId);

        using var _ = await lockRegistry.AcquireAsync(tenantId, routeId, cancellationToken);
        var current = await repository.GetAsync(tenantId, routeId, cancellationToken) ?? throw TransportError.RouteNotFound(routeId);
        RequireVersion(routeId, request.Version, current.Version);

        var stops = await ValidateStopsAsync(tenantId, request.Stops, cancellationToken);
        var updated = current.UpdateDetails(campusId, name, stops, actor.Id, UtcInstant.Now());

        await AuditAsync(tenantId, actor, "transport.route_updated", routeId, "route_updated", correlationId, cancellationToken);
        return await repository.ReplaceAsync(current, updated, CancellationToken.None);
    }

    public async Task<RouteRecord> ActivateAsync(string routeId, VersionedTransportRequest request, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        using var _ = await lockRegistry.AcquireAsync(tenantId, routeId, cancellationToken);
        var current = await repository.GetAsync(tenantId, routeId, cancellationToken) ?? throw TransportError.RouteNotFound(routeId);
        RequireVersion(routeId, request.Version, current.Version);

        if (current.Status != RouteStatus.Draft)
        {
            throw TransportError.InvalidStatusTransition(routeId, current.Status.ToWireString(), RouteStatus.Active.ToWireString());
        }

        var updated = current.Activate(actor.Id, UtcInstant.Now());
        await AuditAsync(tenantId, actor, "transport.route_activated", routeId, "route_activated", correlationId, cancellationToken);
        return await repository.ReplaceAsync(current, updated, CancellationToken.None);
    }

    public async Task<RouteRecord> RetireAsync(string routeId, VersionedTransportRequest request, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        using var _ = await lockRegistry.AcquireAsync(tenantId, routeId, cancellationToken);
        var current = await repository.GetAsync(tenantId, routeId, cancellationToken) ?? throw TransportError.RouteNotFound(routeId);
        RequireVersion(routeId, request.Version, current.Version);

        if (current.Status == RouteStatus.Retired)
        {
            throw TransportError.InvalidStatusTransition(routeId, current.Status.ToWireString(), RouteStatus.Retired.ToWireString());
        }

        var updated = current.Retire(actor.Id, UtcInstant.Now());
        await AuditAsync(tenantId, actor, "transport.route_retired", routeId, "route_retired", correlationId, cancellationToken);
        return await repository.ReplaceAsync(current, updated, CancellationToken.None);
    }

    /// <summary>Every referenced stop must exist, in THIS tenant, and be <see cref="StopStatus.Active"/> - a route cannot be built against a stop that no longer exists or has been retired.</summary>
    private async Task<IReadOnlyList<RouteStopEntry>> ValidateStopsAsync(string tenantId, IReadOnlyList<RouteStopEntryRequest>? requested, CancellationToken cancellationToken)
    {
        if (requested is null || requested.Count == 0)
        {
            return [];
        }

        var entries = new List<RouteStopEntry>();
        foreach (var entry in requested)
        {
            if (string.IsNullOrWhiteSpace(entry.StopId))
            {
                throw TransportError.Validation([new Api.ValidationErrorItem("stops[].stop_id", "required", "each stop entry requires a stop_id")]);
            }

            var stop = await stopRepository.GetAsync(tenantId, entry.StopId, cancellationToken) ?? throw TransportError.StopNotFound(entry.StopId);
            if (stop.Status != StopStatus.Active)
            {
                throw TransportError.StopNotActive(entry.StopId, stop.Status.ToWireString());
            }

            entries.Add(new RouteStopEntry { StopId = entry.StopId, SequenceOrder = entry.SequenceOrder, EstimatedOffsetMinutes = entry.EstimatedOffsetMinutes });
        }

        return entries;
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

    /// <summary>AC04 - shape-validated only (non-empty when present); no Campus registry exists to existence-check against.</summary>
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

    /// <summary>ADR-0003 Decision 7, element 4: a throw from <see cref="ITransportAuditSink.WriteAsync"/> does not by itself prove the event was never persisted - <see cref="ITransportAuditSink.ExistsAsync"/> resolves the ambiguity via ground truth before surfacing <c>audit_unavailable</c>.</summary>
    private async Task AuditAsync(string tenantId, AuditActor actor, string action, string targetId, string reasonCode, string correlationId, CancellationToken cancellationToken)
    {
        var auditEvent = TransportAuditEventBuilder.Build(
            tenantId, actor, action, new AuditTarget("route", targetId), AuditOutcome.Allowed, reasonCode, correlationId, null, new Dictionary<string, (object?, string)>());

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
