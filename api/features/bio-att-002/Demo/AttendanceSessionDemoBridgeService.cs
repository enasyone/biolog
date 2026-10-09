using Biolog.Features.AttendanceSessions.Audit;
using Biolog.Features.AttendanceSessions.Domain;
using Biolog.Features.AttendanceSessions.Identifiers;

namespace Biolog.Features.AttendanceSessions.Demo;

/// <summary>
/// AC05's own bridge: <c>web/features/attendance/live.ts</c>'s two live adapters
/// (<c>attendanceLivePort</c>/<c>attendanceFlagExceptionLivePort</c>) already POST a bare,
/// headerless request to <c>/api/v1/attendance/primary-action</c> and
/// <c>/api/v1/attendance/flag-exception</c> - BIO-UX-010's own scaffolding. BIO-ATT-001 answered
/// these two routes itself first; this story TAKES THEM OVER (disclosed handoff, mirrors
/// BIO-TRN-001 -&gt; BIO-TRN-002's identical precedent) because "View attendance record"/"Flag
/// exception" are far better served by this feature's own session/roster interpretation than by a
/// flat single-person daily status - see <c>docs/features/bio-att-002/README.md</c> "Why this
/// story edits BIO-ATT-001's own endpoint mapping" for the full record.
///
/// This bridge operates against one FIXED, already-public SYNTHETIC demo tenant/session/roster
/// (never a caller-supplied or ambient-resolved value - requiring a tenant/actor on these two
/// specific routes would make them permanently unreachable from the one UI AC05 exists to
/// connect), genuinely exercising this feature's own domain code
/// (<see cref="SessionAttendanceClassifier"/>, <see cref="AttendanceAdjustment.Create"/>) AND, via
/// <see cref="IAttendanceDemoSeedGateway"/>, BIO-ATT-001's own real
/// <c>AttendanceEvent.Capture</c>/event store - a real, connected live implementation, not a
/// second mock.
///
/// <b>Why a dedicated bridge instead of routing through the tenant-scoped services.</b> Those
/// require a real resolved <see cref="Tenancy.ICurrentTenantProvider.TenantId"/> (Scoped, bound to
/// per-request tenant-resolution middleware) - a headerless demo POST resolves none. This bridge is
/// registered Singleton and depends only on Singleton collaborators directly (no captive-dependency
/// risk: none of them has any Scoped dependency of its own).
///
/// Every identifier below is SYNTHETIC and reuses the exact same demo tenant/device ids
/// BIO-ATT-001's own (now-superseded) bridge used - no real tenant, person or device.
/// </summary>
public sealed class AttendanceSessionDemoBridgeService(
    ISessionRepository sessionRepository,
    ISessionAttendancePolicyRepository policyRepository,
    IAttendanceAdjustmentRepository adjustmentRepository,
    ISessionAuditSink auditSink,
    IAttendanceDemoSeedGateway demoSeedGateway)
{
    public const string DemoTenantId = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77";
    public const string DemoUiActorId = "svc_attendance_sessions_ui_bridge";

    private static readonly string[] DemoPersonIds =
    [
        "per_0199e0c1-9d40-7e52-b103-7c9a4e2f6b1d",
        "per_0199e0c2-1111-7222-8333-444455556666",
        "per_0199e0c2-2222-7333-9444-555566667777",
    ];

    private const string DemoDeviceId = "dev_0199e0c2-1b24-7085-9c36-4e9f6a8b2d03";
    private const string DemoSessionTitle = "Intro to Biology — Lecture 12";
    private const string DemoLocation = "Lecture Hall B";

    private readonly SemaphoreSlim _demoSeedGate = new(1, 1);
    private string? _demoSessionId;

    private int _primaryActionCallCount;
    private int _flagExceptionCallCount;

    /// <summary>
    /// "View attendance record" (route-manifest.ts's primary action). Deterministically cycles
    /// the three demo roster members, capturing a genuine, real check-in for whichever one is due
    /// next through BIO-ATT-001's own event store (idempotent within the same day - a repeated
    /// call for the same person/day answers with the SAME already-captured event), then returns a
    /// message describing the REAL, freshly-computed <see cref="SessionMemberClassification"/> for
    /// that roster member - a genuine round trip through this story's own domain logic, not a
    /// canned string.
    /// </summary>
    public async Task<string> ViewNextDemoRosterAsync(CancellationToken cancellationToken)
    {
        var session = await EnsureDemoSessionAsync(cancellationToken);
        var callIndex = Interlocked.Increment(ref _primaryActionCallCount);
        var personId = DemoPersonIds[(callIndex - 1) % DemoPersonIds.Length];

        await demoSeedGateway.CaptureDemoCheckInAsync(DemoTenantId, personId, DemoDeviceId, DemoLocation, session.ScheduledDate, cancellationToken);

        var policy = await policyRepository.GetAsync(DemoTenantId, session.SessionType, cancellationToken);
        var evidence = await demoSeedGateway.ListForPersonOnDateAsync(DemoTenantId, personId, session.ScheduledDate, cancellationToken);
        var classification = SessionAttendanceClassifier.Evaluate(DemoTenantId, session, personId, evidence, policy, UtcInstant.Now());

        return $"Attendance: session '{session.Title}' roster loaded — {personId} is {classification.Status.ToWireString()} for {session.ScheduledDate} (live).";
    }

    /// <summary>
    /// "Flag exception" (route-manifest.ts's secondary action) - records a REAL, audited
    /// <see cref="AttendanceAdjustment"/> against whichever demo roster member this call cycle
    /// lands on, never mutating the underlying classification or any BIO-ATT-001 event.
    /// </summary>
    public async Task<string> FlagNextDemoAdjustmentAsync(CancellationToken cancellationToken)
    {
        var session = await EnsureDemoSessionAsync(cancellationToken);
        var callIndex = Interlocked.Increment(ref _flagExceptionCallCount);
        var personId = DemoPersonIds[(callIndex - 1) % DemoPersonIds.Length];

        var policy = await policyRepository.GetAsync(DemoTenantId, session.SessionType, cancellationToken);
        var evidence = await demoSeedGateway.ListForPersonOnDateAsync(DemoTenantId, personId, session.ScheduledDate, cancellationToken);
        var classification = SessionAttendanceClassifier.Evaluate(DemoTenantId, session, personId, evidence, policy, UtcInstant.Now());

        var (adjustedStatus, reason) = classification.Status == SessionAttendanceStatusValue.Absent
            ? (SessionAttendanceStatusValue.Present, "UI bridge demo: documented absence excused by administrator")
            : (SessionAttendanceStatusValue.Late, "UI bridge demo: flagged for administrator review");

        var adjustment = AttendanceAdjustment.Create(
            DemoTenantId, session.SessionId, personId, classification.Status, adjustedStatus, reason, DemoUiActorId, UtcInstant.Now());

        await WriteAuditAsync(adjustment, cancellationToken);
        adjustment = await adjustmentRepository.CreateAsync(adjustment, cancellationToken);

        return $"Attendance: adjustment recorded for {personId} in session '{session.Title}' — {adjustment.OriginalStatus.ToWireString()} -> {adjustment.AdjustedStatus.ToWireString()} (live).";
    }

    private async Task<Session> EnsureDemoSessionAsync(CancellationToken cancellationToken)
    {
        await _demoSeedGate.WaitAsync(cancellationToken);
        try
        {
            if (_demoSessionId is not null)
            {
                var existing = await sessionRepository.GetAsync(DemoTenantId, _demoSessionId, cancellationToken);
                if (existing is not null)
                {
                    return existing;
                }
            }

            var today = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
            var session = Session.Create(DemoTenantId, SessionType.Lecture, DemoSessionTitle, today, "09:00", DemoPersonIds, DemoUiActorId, UtcInstant.Now());
            session = await sessionRepository.CreateAsync(session, cancellationToken);
            _demoSessionId = session.SessionId;
            return session;
        }
        finally
        {
            _demoSeedGate.Release();
        }
    }

    private async Task WriteAuditAsync(AttendanceAdjustment adjustment, CancellationToken cancellationToken)
    {
        var auditEvent = SessionAuditEventBuilder.Build(
            DemoTenantId,
            new AuditActor(ActorType.ServiceAccount, DemoUiActorId),
            "attendance_session_adjustment.created",
            new AuditTarget("attendance_adjustment", adjustment.AdjustmentId),
            AuditOutcome.Allowed,
            "attendance_session_adjustment_created_via_ui_bridge",
            CanonicalId.NewEventId(),
            null,
            new Dictionary<string, (object?, string)>
            {
                ["person_id"] = (adjustment.PersonId, "internal"),
                ["original_status"] = (adjustment.OriginalStatus.ToWireString(), "internal"),
                ["adjusted_status"] = (adjustment.AdjustedStatus.ToWireString(), "internal"),
            });

        await auditSink.WriteAsync(auditEvent, cancellationToken);
    }
}
