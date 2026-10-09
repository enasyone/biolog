using Biolog.Features.Attendance.Api;
using Biolog.Features.Attendance.Audit;
using Biolog.Features.Attendance.Domain;
using Biolog.Features.Attendance.Tenancy;

namespace Biolog.Features.Attendance;

/// <summary>
/// Application service for AC04: reading and setting the tenant-wide attendance interpretation
/// policy. Implements the same audit-atomicity pattern as <see cref="AttendanceEventService"/>.
///
/// <b>This is the one authorization decision this feature makes (ADR-0006 Decision 1/2).</b>
/// Setting the policy changes how EVERY person's raw events in the tenant get classified going
/// forward - a capability, gated by identity, not merely an audit-attribution record of who
/// changed it. <see cref="ManagePermission"/> is required via <see cref="IPermissionGateway"/>
/// (this feature's own RBAC integration seam, wrapping BIO-IDN-006's
/// <c>PermissionService.ActorHasPermissionAsync</c> directly), and the endpoint additionally
/// requires a verified ADR-0006 Decision 3 provenance proof for the asserted actor id
/// (<see cref="Api.ActorProvenanceGate"/>) - the permission check alone would be exactly Round 1's
/// mistake in BIO-IDN-006 (ZA-IDN006-01): a capability check keyed on a caller-controlled,
/// unverified identity claim is not a real gate. <see cref="GetAsync"/> stays open (no permission
/// or provenance check) - reading the CURRENT policy discloses no more than what
/// <see cref="AttendanceStatusEvaluator"/> already applies to every derived-status read, which
/// this feature leaves open per ADR-0006 Decision 2 (see <see cref="AttendanceEventService"/>'s
/// own header).
/// </summary>
internal sealed class AttendancePolicyService(
    ICurrentTenantProvider tenantProvider,
    IAttendancePolicyRepository repository,
    IAttendanceAuditSink auditSink,
    AttendancePolicyLockRegistry lockRegistry,
    IPermissionGateway permissionGateway)
{
    public const string ManagePermission = "attendance.policy_manage";

    public async Task<AttendancePolicy> GetAsync(CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        return await repository.GetAsync(tenantId, cancellationToken);
    }

    public async Task<AttendancePolicy> SetAsync(SetAttendancePolicyRequest request, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();

        if (!await permissionGateway.ActorHasPermissionAsync(tenantId, actor.Id, ManagePermission, cancellationToken))
        {
            throw AttendanceError.PermissionRequired(ManagePermission);
        }

        if (!AttendanceInterpretationModeExtensions.TryParse(request.InterpretationMode, out var interpretationMode))
        {
            throw AttendanceError.Validation([new ValidationErrorItem("interpretation_mode", "invalid", "interpretation_mode must be one of education, workforce, presence")]);
        }

        using var _ = await lockRegistry.AcquireAsync(tenantId, cancellationToken);

        var current = await repository.GetAsync(tenantId, cancellationToken);
        var updated = current.Version == 0
            ? AttendancePolicy.Create(tenantId, interpretationMode, request.ExpectedStartTime, request.LateGraceMinutes, actor.Id, Identifiers.UtcInstant.Now())
            : current.Update(interpretationMode, request.ExpectedStartTime, request.LateGraceMinutes, actor.Id, Identifiers.UtcInstant.Now());

        await AuditAsync(tenantId, actor, "attendance.policy_updated", correlationId,
            new Dictionary<string, (object?, string)>
            {
                ["interpretation_mode"] = (interpretationMode.ToWireString(), "internal"),
                ["version"] = (updated.Version, "internal"),
            },
            cancellationToken);

        if (!await repository.TrySaveAsync(updated, current.Version, CancellationToken.None))
        {
            throw new AttendanceError(
                "the attendance policy was modified since the version this request expected", "concurrency_conflict", 409,
                new Dictionary<string, object?> { ["expected_version"] = current.Version });
        }

        return updated;
    }

    private async Task AuditAsync(
        string tenantId, AuditActor actor, string action, string correlationId,
        IReadOnlyDictionary<string, (object? Value, string Classification)> detail, CancellationToken cancellationToken)
    {
        var auditEvent = AttendanceAuditEventBuilder.Build(
            tenantId, actor, action, new AuditTarget("attendance_policy", tenantId), AuditOutcome.Allowed, "attendance_policy_updated", correlationId, null, detail);

        try
        {
            await auditSink.WriteAsync(auditEvent, cancellationToken);
        }
        catch
        {
            if (!await auditSink.ExistsAsync(auditEvent.EventId, CancellationToken.None))
            {
                throw AttendanceError.AuditUnavailable();
            }
        }
    }

    private string RequireTenant() => tenantProvider.TenantId ?? throw AttendanceError.TenantContextMissing();
}
