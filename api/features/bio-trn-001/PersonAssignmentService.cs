using Biolog.Features.Transport.Api;
using Biolog.Features.Transport.Audit;
using Biolog.Features.Transport.Domain;
using Biolog.Features.Transport.Identifiers;
using Biolog.Features.Transport.Tenancy;

namespace Biolog.Features.Transport;

/// <summary>
/// AC02/AC04: person-to-route/stop assignment with a validity window. Implements the same
/// audit-atomicity pattern as the other three services.
///
/// <b><see cref="CreateAsync"/>/<see cref="EndAsync"/> are this feature's one authorization
/// decision (ADR-0006 Decision 1/2).</b> Deciding a specific person may board a specific vehicle
/// on a specific route is granting physical transport access, requiring <see cref="AssignPermission"/>
/// via <see cref="IPermissionGateway"/> AND a verified ADR-0006 Decision 3 provenance proof
/// (<see cref="Api.ActorProvenanceGate"/>). <see cref="GetAsync"/>/<see cref="ListAsync"/> stay
/// open - reading an assignment discloses no more than the route/stop registry itself already
/// does.
///
/// <b>AC02's overlap invariant.</b> At most one assignment per <c>(TenantId, PersonId)</c> may
/// have a validity window overlapping another's. This is a tenant/person-scoped invariant across
/// MULTIPLE records, not a single record's own state, so <see cref="PersonAssignmentUniquenessLockRegistry"/>
/// (keyed by <c>(TenantId, PersonId)</c>) is acquired FIRST, the overlap checked under that lock,
/// then the new record created - the exact BIO-IDN-003 lesson (lock scope must match invariant
/// scope) re-applied, not rediscovered.
/// </summary>
internal sealed class PersonAssignmentService(
    ICurrentTenantProvider tenantProvider,
    IPersonAssignmentRepository repository,
    IRouteRepository routeRepository,
    IStopRepository stopRepository,
    ITransportAuditSink auditSink,
    PersonAssignmentLockRegistry lockRegistry,
    PersonAssignmentUniquenessLockRegistry uniquenessLockRegistry,
    IPersonExistenceGateway personGateway,
    IPermissionGateway permissionGateway)
{
    public const string AssignPermission = "transport.assign_manage";

    public async Task<PersonAssignment> CreateAsync(CreatePersonAssignmentRequest request, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();

        if (!await permissionGateway.ActorHasPermissionAsync(tenantId, actor.Id, AssignPermission, cancellationToken))
        {
            throw TransportError.PermissionRequired(AssignPermission);
        }

        var errors = new List<Api.ValidationErrorItem>();
        var personId = RequireField(request.PersonId, "person_id", errors);
        var routeId = RequireField(request.RouteId, "route_id", errors);
        var stopId = RequireField(request.StopId, "stop_id", errors);
        var validFrom = RequireField(request.ValidFrom, "valid_from", errors);
        ThrowIfInvalid(errors);

        if (!UtcInstant.TryParse(validFrom, out var validFromInstant))
        {
            throw TransportError.Validation([new Api.ValidationErrorItem("valid_from", "invalid_format", "valid_from must be an RFC 3339 UTC instant")]);
        }

        DateTime? validUntilInstant = null;
        if (request.ValidUntil is not null)
        {
            if (!UtcInstant.TryParse(request.ValidUntil, out var parsed))
            {
                throw TransportError.Validation([new Api.ValidationErrorItem("valid_until", "invalid_format", "valid_until must be an RFC 3339 UTC instant when present")]);
            }

            if (parsed <= validFromInstant)
            {
                throw TransportError.Validation([new Api.ValidationErrorItem("valid_until", "out_of_range", "valid_until must be after valid_from")]);
            }

            validUntilInstant = parsed;
        }

        if (!await personGateway.ExistsAsync(personId!, cancellationToken))
        {
            throw TransportError.PersonNotFound(personId!);
        }

        var route = await routeRepository.GetAsync(tenantId, routeId!, cancellationToken) ?? throw TransportError.RouteNotFound(routeId!);
        if (route.Status != RouteStatus.Active)
        {
            throw TransportError.RouteNotActive(routeId!, route.Status.ToWireString());
        }

        if (!route.Stops.Any(s => s.StopId == stopId))
        {
            throw TransportError.StopNotOnRoute(stopId!, routeId!);
        }

        var stop = await stopRepository.GetAsync(tenantId, stopId!, cancellationToken) ?? throw TransportError.StopNotFound(stopId!);
        if (stop.Status != StopStatus.Active)
        {
            throw TransportError.StopNotActive(stopId!, stop.Status.ToWireString());
        }

        // AC04 - the one real campus-consistency rule: when BOTH the route and the stop declare a
        // campus, they must agree. Neither declaring one, or only one declaring one, is not a
        // mismatch (campus scoping is optional, not mandatory - see docs/features/bio-trn-001/README.md).
        if (route.CampusId is not null && stop.CampusId is not null && route.CampusId != stop.CampusId)
        {
            throw TransportError.CampusMismatch(route.CampusId, stop.CampusId);
        }

        var now = UtcInstant.Now();

        // AC02 - the overlap lock is scoped to (tenant, person), acquired BEFORE any per-record
        // lock (there is none yet for a brand-new assignment) and before the new record is even
        // constructed, so two concurrent CreateAsync calls for the same person genuinely
        // serialize on the overlap check itself, not just on the eventual repository write.
        using var _ = await uniquenessLockRegistry.AcquireAsync(tenantId, personId!, cancellationToken);

        var existing = await repository.ListByPersonAsync(tenantId, personId!, cancellationToken);
        var conflict = existing.FirstOrDefault(a => WindowsOverlap(validFromInstant, validUntilInstant, a));
        if (conflict is not null)
        {
            throw TransportError.OverlappingAssignment(personId!, conflict.Id);
        }

        var record = PersonAssignment.Create(tenantId, personId!, routeId!, stopId!, validFrom!, request.ValidUntil, actor.Id, now);

        await AuditAsync(tenantId, actor, "transport.assignment_created", record.Id, "assignment_created", correlationId,
            new Dictionary<string, (object?, string)> { ["person_id"] = (personId, "internal"), ["route_id"] = (routeId, "internal") },
            cancellationToken);

        return await repository.CreateAsync(record, CancellationToken.None);
    }

    public async Task<PersonAssignment> GetAsync(string assignmentId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        return await repository.GetAsync(tenantId, assignmentId, cancellationToken) ?? throw TransportError.AssignmentNotFound(assignmentId);
    }

    public Task<IReadOnlyList<PersonAssignment>> ListAsync(CancellationToken cancellationToken = default) =>
        repository.ListByTenantAsync(RequireTenant(), cancellationToken);

    public async Task<PersonAssignment> EndAsync(string assignmentId, EndPersonAssignmentRequest request, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();

        if (!await permissionGateway.ActorHasPermissionAsync(tenantId, actor.Id, AssignPermission, cancellationToken))
        {
            throw TransportError.PermissionRequired(AssignPermission);
        }

        using var _ = await lockRegistry.AcquireAsync(tenantId, assignmentId, cancellationToken);
        var current = await repository.GetAsync(tenantId, assignmentId, cancellationToken) ?? throw TransportError.AssignmentNotFound(assignmentId);

        if (request.Version is not int expectedVersion)
        {
            throw TransportError.Validation([new Api.ValidationErrorItem("version", "required", "version is required so a stale write can be detected")]);
        }

        if (expectedVersion != current.Version)
        {
            throw TransportError.ConcurrencyConflict(assignmentId, expectedVersion, current.Version);
        }

        var now = UtcInstant.Now();
        if (TransportStatusEvaluator.EffectiveStatus(current, now) == EffectiveAssignmentStatus.Ended)
        {
            throw TransportError.AssignmentAlreadyEnded(assignmentId);
        }

        var endAt = request.EndAt ?? now;
        var updated = current.End(endAt);

        await AuditAsync(tenantId, actor, "transport.assignment_ended", assignmentId, "assignment_ended", correlationId, new Dictionary<string, (object?, string)>(), cancellationToken);
        return await repository.ReplaceAsync(current, updated, CancellationToken.None);
    }

    /// <summary>Half-open interval overlap: <c>[from, until)</c>, with a <c>null</c> until treated as +infinity. Two windows overlap iff each one's start is strictly before the other's end.</summary>
    private static bool WindowsOverlap(DateTime newFrom, DateTime? newUntil, PersonAssignment existing)
    {
        if (!UtcInstant.TryParse(existing.ValidFrom, out var existingFrom))
        {
            return false; // unreachable for a validly-stored record, but never treat a malformed record as blocking
        }

        DateTime? existingUntil = null;
        if (existing.ValidUntil is not null && UtcInstant.TryParse(existing.ValidUntil, out var parsed))
        {
            existingUntil = parsed;
        }

        var newStartsBeforeExistingEnds = existingUntil is null || newFrom < existingUntil;
        var existingStartsBeforeNewEnds = newUntil is null || existingFrom < newUntil;
        return newStartsBeforeExistingEnds && existingStartsBeforeNewEnds;
    }

    private static string? RequireField(string? value, string field, List<Api.ValidationErrorItem> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(new Api.ValidationErrorItem(field, "required", $"{field} is required"));
            return null;
        }

        return value.Trim();
    }

    private static void ThrowIfInvalid(List<Api.ValidationErrorItem> errors)
    {
        if (errors.Count > 0)
        {
            throw TransportError.Validation(errors);
        }
    }

    private async Task AuditAsync(
        string tenantId, AuditActor actor, string action, string targetId, string reasonCode, string correlationId,
        IReadOnlyDictionary<string, (object? Value, string Classification)> detail, CancellationToken cancellationToken)
    {
        var auditEvent = TransportAuditEventBuilder.Build(
            tenantId, actor, action, new AuditTarget("person_assignment", targetId), AuditOutcome.Allowed, reasonCode, correlationId, null, detail);

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
