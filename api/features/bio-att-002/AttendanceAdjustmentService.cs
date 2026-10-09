using Biolog.Features.AttendanceSessions.Api;
using Biolog.Features.AttendanceSessions.Audit;
using Biolog.Features.AttendanceSessions.Domain;
using Biolog.Features.AttendanceSessions.Identifiers;
using Biolog.Features.AttendanceSessions.Tenancy;

namespace Biolog.Features.AttendanceSessions;

/// <summary>
/// Application service for AC04: recording a manual override of one roster member's
/// classification for one session.
///
/// <b>This is this feature's other authorization decision (ADR-0006 Decision 1/2).</b> An
/// adjustment overrides what every subsequent roster read shows as that person's effective status
/// (<see cref="SessionRosterMemberView.EffectiveStatus"/>) - a capability, gated by identity,
/// exactly like <see cref="SessionAttendancePolicyService.SetAsync"/>. <see cref="ManagePermission"/>
/// is required via <see cref="IPermissionGateway"/>, and the endpoint additionally requires a
/// verified ADR-0006 Decision 3 provenance proof for the asserted actor id.
///
/// <b>Never mutates anything.</b> The original computed <see cref="SessionMemberClassification"/>
/// and every BIO-ATT-001 <c>AttendanceEvent</c> it was grounded on stay exactly as they were - this
/// service only APPENDS a new <see cref="AttendanceAdjustment"/> record, the same two-tier
/// never-overwrite shape <c>bio-doc-009</c>'s own <c>AuthenticityManualOverride</c> established.
/// Implements the same audit-atomicity pattern as <see cref="SessionService"/>.
/// </summary>
internal sealed class AttendanceAdjustmentService(
    ICurrentTenantProvider tenantProvider,
    ISessionRepository sessionRepository,
    IAttendanceAdjustmentRepository adjustmentRepository,
    ISessionAuditSink auditSink,
    IPermissionGateway permissionGateway,
    SessionAttendanceQueryService queryService)
{
    public const string ManagePermission = "attendance.session_adjustment_manage";

    public async Task<AttendanceAdjustment> CreateAdjustmentAsync(
        string sessionId, CreateAttendanceAdjustmentRequest request, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();

        if (!await permissionGateway.ActorHasPermissionAsync(tenantId, actor.Id, ManagePermission, cancellationToken))
        {
            throw SessionError.PermissionRequired(ManagePermission);
        }

        var session = await sessionRepository.GetAsync(tenantId, sessionId, cancellationToken) ?? throw SessionError.SessionNotFound(sessionId);

        if (!session.RosterPersonIds.Contains(request.PersonId, StringComparer.Ordinal))
        {
            throw SessionError.PersonNotOnRoster(sessionId, request.PersonId);
        }

        if (!SessionAttendanceStatusValueExtensions.TryParse(request.AdjustedStatus, out var adjustedStatus))
        {
            throw SessionError.Validation([new ValidationErrorItem("adjusted_status", "invalid", "adjusted_status must be one of present, late, absent")]);
        }

        // AC04's own "original status" is whatever the classifier currently computes - captured
        // fresh here, never itself mutated by this write.
        var currentView = await queryService.GetMemberAsync(session, request.PersonId, cancellationToken);

        var adjustment = AttendanceAdjustment.Create(
            tenantId, sessionId, request.PersonId, currentView.EffectiveStatus, adjustedStatus, request.Reason, actor.Id, UtcInstant.Now());

        await AuditAsync(tenantId, actor, adjustment, correlationId, cancellationToken);

        return await adjustmentRepository.CreateAsync(adjustment, CancellationToken.None);
    }

    private async Task AuditAsync(string tenantId, AuditActor actor, AttendanceAdjustment adjustment, string correlationId, CancellationToken cancellationToken)
    {
        var auditEvent = SessionAuditEventBuilder.Build(
            tenantId, actor, "attendance_session_adjustment.created", new AuditTarget("attendance_adjustment", adjustment.AdjustmentId), AuditOutcome.Allowed,
            "attendance_session_adjustment_created", correlationId, null,
            new Dictionary<string, (object?, string)>
            {
                ["session_id"] = (adjustment.SessionId, "internal"),
                ["person_id"] = (adjustment.PersonId, "internal"),
                ["original_status"] = (adjustment.OriginalStatus.ToWireString(), "internal"),
                ["adjusted_status"] = (adjustment.AdjustedStatus.ToWireString(), "internal"),
            });

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
