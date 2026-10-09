using Biolog.Features.AttendanceSessions.Api;
using Biolog.Features.AttendanceSessions.Audit;
using Biolog.Features.AttendanceSessions.Domain;
using Xunit;

namespace Biolog.Features.AttendanceSessions.Tests;

public sealed class AttendanceAdjustmentServiceTests
{
    private const string TenantId = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77";
    private const string PersonA = "per_0199e0c1-9d40-7e52-b103-7c9a4e2f6b1d";
    private const string PersonNotOnRoster = "per_0199e0c2-9999-7999-8999-999999999999";
    private static readonly AuditActor Actor = new(ActorType.User, "usr_admin_1");

    private static (AttendanceAdjustmentService Service, Session Session, InMemoryAttendanceAdjustmentRepository Adjustments, InMemorySessionAuditSink AuditSink, FakePermissionGateway PermissionGateway) Build()
    {
        var sessions = new InMemorySessionRepository();
        var policies = new InMemorySessionAttendancePolicyRepository();
        var adjustments = new InMemoryAttendanceAdjustmentRepository();
        var auditSink = new InMemorySessionAuditSink();
        var permissionGateway = new FakePermissionGateway();
        var evidence = new FakeAttendanceEventQueryGateway();
        var tenantProvider = new FakeCurrentTenantProvider(TenantId);

        var session = Session.Create(TenantId, SessionType.Lecture, "Intro to Biology", "2026-10-01", "09:00", [PersonA], "usr_teacher", "2026-09-30T00:00:00.000Z");
        sessions.CreateAsync(session, CancellationToken.None).GetAwaiter().GetResult();

        var queryService = new SessionAttendanceQueryService(tenantProvider, sessions, policies, adjustments, evidence);
        var service = new AttendanceAdjustmentService(tenantProvider, sessions, adjustments, auditSink, permissionGateway, queryService);

        return (service, session, adjustments, auditSink, permissionGateway);
    }

    [Fact]
    public async Task AC04_creating_an_adjustment_without_the_manage_permission_is_rejected()
    {
        var (service, session, _, _, _) = Build();

        var error = await Assert.ThrowsAsync<SessionError>(() => service.CreateAdjustmentAsync(session.SessionId,
            new CreateAttendanceAdjustmentRequest { PersonId = PersonA, AdjustedStatus = "present", Reason = "excused" },
            Actor, "evt_0199e0c1-0000-7000-8000-000000000020", CancellationToken.None));

        Assert.Equal("permission_required", error.Code);
    }

    [Fact]
    public async Task AC04_an_adjustment_targeting_a_person_not_on_the_roster_is_rejected()
    {
        var (service, session, _, _, permissionGateway) = Build();
        permissionGateway.GrantedActorIds.Add(Actor.Id);

        var error = await Assert.ThrowsAsync<SessionError>(() => service.CreateAdjustmentAsync(session.SessionId,
            new CreateAttendanceAdjustmentRequest { PersonId = PersonNotOnRoster, AdjustedStatus = "present", Reason = "excused" },
            Actor, "evt_0199e0c1-0000-7000-8000-000000000021", CancellationToken.None));

        Assert.Equal("person_not_on_roster", error.Code);
    }

    [Fact]
    public async Task AC04_a_granted_actor_can_record_a_real_audited_adjustment_capturing_the_true_original_status()
    {
        var (service, session, adjustments, auditSink, permissionGateway) = Build();
        permissionGateway.GrantedActorIds.Add(Actor.Id);

        var adjustment = await service.CreateAdjustmentAsync(session.SessionId,
            new CreateAttendanceAdjustmentRequest { PersonId = PersonA, AdjustedStatus = "present", Reason = "documented illness, excused" },
            Actor, "evt_0199e0c1-0000-7000-8000-000000000022", CancellationToken.None);

        // No evidence was ever seeded for PersonA, so the true, freshly-computed original status is Absent.
        Assert.Equal(SessionAttendanceStatusValue.Absent, adjustment.OriginalStatus);
        Assert.Equal(SessionAttendanceStatusValue.Present, adjustment.AdjustedStatus);
        Assert.Equal(Actor.Id, adjustment.AdjustedByActorId);

        var persisted = await adjustments.ListForSessionAsync(TenantId, session.SessionId, CancellationToken.None);
        Assert.Single(persisted);
        Assert.Contains(auditSink.All(), e => e.Action == "attendance_session_adjustment.created");
    }

    [Fact]
    public async Task AC04_an_adjustment_with_an_invalid_status_is_rejected()
    {
        var (service, session, _, _, permissionGateway) = Build();
        permissionGateway.GrantedActorIds.Add(Actor.Id);

        var error = await Assert.ThrowsAsync<SessionError>(() => service.CreateAdjustmentAsync(session.SessionId,
            new CreateAttendanceAdjustmentRequest { PersonId = PersonA, AdjustedStatus = "not-a-real-status", Reason = "typo" },
            Actor, "evt_0199e0c1-0000-7000-8000-000000000023", CancellationToken.None));

        Assert.Equal("validation_failed", error.Code);
    }

    [Fact]
    public async Task AC04_an_adjustment_with_an_empty_reason_is_rejected()
    {
        var (service, session, _, _, permissionGateway) = Build();
        permissionGateway.GrantedActorIds.Add(Actor.Id);

        var error = await Assert.ThrowsAsync<SessionError>(() => service.CreateAdjustmentAsync(session.SessionId,
            new CreateAttendanceAdjustmentRequest { PersonId = PersonA, AdjustedStatus = "present", Reason = "  " },
            Actor, "evt_0199e0c1-0000-7000-8000-000000000024", CancellationToken.None));

        Assert.Equal("validation_failed", error.Code);
    }
}
