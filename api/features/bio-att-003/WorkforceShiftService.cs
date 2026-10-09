using Biolog.Features.WorkforceAttendance.Api;
using Biolog.Features.WorkforceAttendance.Audit;
using Biolog.Features.WorkforceAttendance.Domain;
using Biolog.Features.WorkforceAttendance.Identifiers;
using Biolog.Features.WorkforceAttendance.Tenancy;

namespace Biolog.Features.WorkforceAttendance;

/// <summary>
/// Application service for AC01: creating, reading and updating a tenant's named shift
/// schedules. Implements the same audit-atomicity pattern as every sibling story's own
/// policy-management service (audit written and confirmed durable BEFORE the commit).
///
/// <b>This feature's first authorization decision (ADR-0006 Decision 1/2).</b> Creating or
/// updating a shift changes how EVERY person evaluated against it gets classified going
/// forward - a capability, gated by identity, not merely an audit-attribution record of who
/// changed it. <see cref="ManagePermission"/> is required via <see cref="IPermissionGateway"/>,
/// and the endpoint additionally requires a verified ADR-0006 Decision 3 provenance proof for
/// the asserted actor id. <see cref="GetAsync"/>/<see cref="ListAsync"/> stay open - reading the
/// current shift catalogue discloses no more than what every daily-summary read already does.
/// </summary>
internal sealed class WorkforceShiftService(
    ICurrentTenantProvider tenantProvider,
    IWorkforceShiftRepository repository,
    IWorkforceAttendanceAuditSink auditSink,
    WorkforceShiftLockRegistry lockRegistry,
    IPermissionGateway permissionGateway)
{
    public const string ManagePermission = "workforce_attendance.shift_manage";

    public async Task<WorkforceShift?> GetAsync(string shiftId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        return await repository.GetAsync(tenantId, shiftId, cancellationToken);
    }

    public async Task<IReadOnlyList<WorkforceShift>> ListAsync(CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        return await repository.ListAsync(tenantId, cancellationToken);
    }

    public async Task<WorkforceShift> CreateAsync(CreateWorkforceShiftRequest request, Audit.AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();

        if (!await permissionGateway.ActorHasPermissionAsync(tenantId, actor.Id, ManagePermission, cancellationToken))
        {
            throw WorkforceAttendanceError.PermissionRequired(ManagePermission);
        }

        var shift = WorkforceShift.Create(tenantId, request.Name, request.StartTime, request.EndTime, request.GraceMinutes, actor.Id, UtcInstant.Now());

        using var _ = await lockRegistry.AcquireAsync(tenantId, shift.ShiftId, cancellationToken);

        await AuditAsync(tenantId, actor, "workforce_attendance.shift_created", shift.ShiftId, correlationId,
            new Dictionary<string, (object?, string)>
            {
                ["name"] = (shift.Name, "internal"),
                ["start_time"] = (shift.StartTime, "internal"),
                ["end_time"] = (shift.EndTime, "internal"),
                ["grace_minutes"] = (shift.GraceMinutes, "internal"),
                ["version"] = (shift.Version, "internal"),
            },
            cancellationToken);

        return await repository.CreateAsync(shift, CancellationToken.None);
    }

    public async Task<WorkforceShift> UpdateAsync(string shiftId, UpdateWorkforceShiftRequest request, Audit.AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();

        if (!await permissionGateway.ActorHasPermissionAsync(tenantId, actor.Id, ManagePermission, cancellationToken))
        {
            throw WorkforceAttendanceError.PermissionRequired(ManagePermission);
        }

        using var _ = await lockRegistry.AcquireAsync(tenantId, shiftId, cancellationToken);

        var current = await repository.GetAsync(tenantId, shiftId, cancellationToken) ?? throw WorkforceAttendanceError.ShiftNotFound(shiftId);

        if (request.Version != current.Version)
        {
            throw WorkforceAttendanceError.ConcurrencyConflict("shift", current.Version);
        }

        var updated = current.Update(request.Name, request.StartTime, request.EndTime, request.GraceMinutes, actor.Id, UtcInstant.Now());

        await AuditAsync(tenantId, actor, "workforce_attendance.shift_updated", shiftId, correlationId,
            new Dictionary<string, (object?, string)>
            {
                ["name"] = (updated.Name, "internal"),
                ["start_time"] = (updated.StartTime, "internal"),
                ["end_time"] = (updated.EndTime, "internal"),
                ["grace_minutes"] = (updated.GraceMinutes, "internal"),
                ["version"] = (updated.Version, "internal"),
            },
            cancellationToken);

        if (!await repository.TrySaveAsync(updated, current.Version, CancellationToken.None))
        {
            throw WorkforceAttendanceError.ConcurrencyConflict("shift", current.Version);
        }

        return updated;
    }

    private async Task AuditAsync(
        string tenantId, Audit.AuditActor actor, string action, string shiftId, string correlationId,
        IReadOnlyDictionary<string, (object? Value, string Classification)> detail, CancellationToken cancellationToken)
    {
        var auditEvent = WorkforceAttendanceAuditEventBuilder.Build(
            tenantId, actor, action, new AuditTarget("workforce_shift", shiftId), AuditOutcome.Allowed, action, correlationId, causationId: null, detail);

        try
        {
            await auditSink.WriteAsync(auditEvent, cancellationToken);
            return;
        }
        catch (Exception)
        {
        }

        bool exists;
        try
        {
            exists = await auditSink.ExistsAsync(auditEvent.EventId, cancellationToken);
        }
        catch (Exception)
        {
            throw WorkforceAttendanceError.AuditUnavailable();
        }

        if (!exists)
        {
            throw WorkforceAttendanceError.AuditUnavailable();
        }
    }

    private string RequireTenant() => tenantProvider.TenantId ?? throw WorkforceAttendanceError.TenantContextMissing();
}
