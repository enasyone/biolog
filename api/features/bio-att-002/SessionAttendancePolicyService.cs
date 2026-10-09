using Biolog.Features.AttendanceSessions.Api;
using Biolog.Features.AttendanceSessions.Audit;
using Biolog.Features.AttendanceSessions.Domain;
using Biolog.Features.AttendanceSessions.Identifiers;
using Biolog.Features.AttendanceSessions.Tenancy;

namespace Biolog.Features.AttendanceSessions;

/// <summary>
/// Application service for AC02: reading and setting a (tenant, session type) pair's present/late/
/// absent thresholds. Implements the same audit-atomicity pattern as <see cref="SessionService"/>.
///
/// <b>This is one of this feature's two authorization decisions (ADR-0006 Decision 1/2).</b>
/// Setting a session type's policy changes how EVERY roster member's raw events across EVERY
/// session of that type in the tenant get classified going forward - a capability, gated by
/// identity, not merely an audit-attribution record of who changed it. <see cref="ManagePermission"/>
/// is required via <see cref="IPermissionGateway"/>, and the endpoint additionally requires a
/// verified ADR-0006 Decision 3 provenance proof for the asserted actor id
/// (<see cref="Api.ActorProvenanceGate"/>) - the permission check alone would be exactly BIO-IDN-006
/// Round 1's mistake (ZA-IDN006-01): a capability check keyed on a caller-controlled, unverified
/// identity claim is not a real gate. <see cref="GetAsync"/> stays open (no permission or provenance
/// check) - reading the CURRENT policy discloses no more than what a roster read already applies.
/// </summary>
internal sealed class SessionAttendancePolicyService(
    ICurrentTenantProvider tenantProvider,
    ISessionAttendancePolicyRepository repository,
    ISessionAuditSink auditSink,
    SessionAttendancePolicyLockRegistry lockRegistry,
    IPermissionGateway permissionGateway)
{
    public const string ManagePermission = "attendance.session_policy_manage";

    public async Task<SessionAttendancePolicy> GetAsync(string sessionTypeWire, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        var sessionType = ParseSessionType(sessionTypeWire);
        return await repository.GetAsync(tenantId, sessionType, cancellationToken);
    }

    public async Task<SessionAttendancePolicy> SetAsync(string sessionTypeWire, SetSessionAttendancePolicyRequest request, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        var sessionType = ParseSessionType(sessionTypeWire);

        if (!await permissionGateway.ActorHasPermissionAsync(tenantId, actor.Id, ManagePermission, cancellationToken))
        {
            throw SessionError.PermissionRequired(ManagePermission);
        }

        using var _ = await lockRegistry.AcquireAsync(tenantId, sessionType, cancellationToken);

        var current = await repository.GetAsync(tenantId, sessionType, cancellationToken);
        var updated = current.Version == 0
            ? SessionAttendancePolicy.Create(tenantId, sessionType, request.LateGraceMinutes, request.AbsentAfterMinutes, actor.Id, UtcInstant.Now())
            : current.Update(request.LateGraceMinutes, request.AbsentAfterMinutes, actor.Id, UtcInstant.Now());

        await AuditAsync(tenantId, actor, "attendance_session_policy.updated", sessionType, correlationId,
            new Dictionary<string, (object?, string)>
            {
                ["session_type"] = (sessionType.ToWireString(), "internal"),
                ["late_grace_minutes"] = (updated.LateGraceMinutes, "internal"),
                ["absent_after_minutes"] = (updated.AbsentAfterMinutes, "internal"),
                ["version"] = (updated.Version, "internal"),
            },
            cancellationToken);

        if (!await repository.TrySaveAsync(updated, current.Version, CancellationToken.None))
        {
            throw SessionError.ConcurrencyConflict("session attendance policy", current.Version);
        }

        return updated;
    }

    private static SessionType ParseSessionType(string? sessionTypeWire)
    {
        if (!SessionTypeExtensions.TryParse(sessionTypeWire, out var sessionType))
        {
            throw SessionError.Validation([new ValidationErrorItem("session_type", "invalid", "session_type must be one of lecture, class, exam, hostel, event")]);
        }

        return sessionType;
    }

    private async Task AuditAsync(
        string tenantId, AuditActor actor, string action, SessionType sessionType, string correlationId,
        IReadOnlyDictionary<string, (object? Value, string Classification)> detail, CancellationToken cancellationToken)
    {
        var auditEvent = SessionAuditEventBuilder.Build(
            tenantId, actor, action, new AuditTarget("attendance_session_policy", $"{tenantId}:{sessionType.ToWireString()}"), AuditOutcome.Allowed,
            "attendance_session_policy_updated", correlationId, null, detail);

        try
        {
            await auditSink.WriteAsync(auditEvent, cancellationToken);
        }
        catch
        {
            if (!await auditSink.ExistsAsync(auditEvent.EventId, CancellationToken.None))
            {
                throw SessionError.AuditUnavailable();
            }
        }
    }

    private string RequireTenant() => tenantProvider.TenantId ?? throw SessionError.TenantContextMissing();
}
