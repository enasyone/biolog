using Biolog.Features.AttendanceSessions.Api;
using Biolog.Features.AttendanceSessions.Audit;
using Biolog.Features.AttendanceSessions.Domain;
using Xunit;

namespace Biolog.Features.AttendanceSessions.Tests;

public sealed class SessionAttendancePolicyServiceTests
{
    private const string TenantId = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77";
    private static readonly AuditActor Actor = new(ActorType.User, "usr_admin_1");

    private static SessionAttendancePolicyService BuildService(out FakePermissionGateway permissionGateway, out InMemorySessionAuditSink auditSink)
    {
        permissionGateway = new FakePermissionGateway();
        auditSink = new InMemorySessionAuditSink();
        return new SessionAttendancePolicyService(
            new FakeCurrentTenantProvider(TenantId), new InMemorySessionAttendancePolicyRepository(), auditSink,
            new SessionAttendancePolicyLockRegistry(), permissionGateway);
    }

    [Fact]
    public async Task AC02_a_tenant_with_no_explicit_policy_still_has_an_effective_default()
    {
        var service = BuildService(out _, out _);
        var policy = await service.GetAsync("lecture", CancellationToken.None);

        Assert.Equal(0, policy.Version);
        Assert.Equal(15, policy.LateGraceMinutes);
        Assert.Equal(30, policy.AbsentAfterMinutes);
    }

    [Fact]
    public async Task AC02_setting_a_policy_without_the_manage_permission_is_rejected()
    {
        var service = BuildService(out _, out _);

        var error = await Assert.ThrowsAsync<SessionError>(() => service.SetAsync("exam",
            new SetSessionAttendancePolicyRequest { LateGraceMinutes = 5, AbsentAfterMinutes = 10 },
            Actor, "evt_0199e0c1-0000-7000-8000-000000000010", CancellationToken.None));

        Assert.Equal("permission_required", error.Code);
    }

    [Fact]
    public async Task AC02_a_granted_actor_can_set_the_policy_and_it_is_richer_than_a_single_threshold()
    {
        var service = BuildService(out var permissionGateway, out _);
        permissionGateway.GrantedActorIds.Add(Actor.Id);

        var policy = await service.SetAsync("exam",
            new SetSessionAttendancePolicyRequest { LateGraceMinutes = 5, AbsentAfterMinutes = 10 },
            Actor, "evt_0199e0c1-0000-7000-8000-000000000011", CancellationToken.None);

        Assert.Equal(1, policy.Version);
        Assert.Equal(5, policy.LateGraceMinutes);
        Assert.Equal(10, policy.AbsentAfterMinutes);

        // A DIFFERENT session type in the SAME tenant still has the untouched default - genuine
        // per-(tenant, session type) granularity, not one shared tenant-wide threshold.
        var lecturePolicy = await service.GetAsync("lecture", CancellationToken.None);
        Assert.Equal(0, lecturePolicy.Version);
    }

    [Fact]
    public async Task AC02_absent_after_minutes_must_be_at_least_late_grace_minutes()
    {
        var service = BuildService(out var permissionGateway, out _);
        permissionGateway.GrantedActorIds.Add(Actor.Id);

        var error = await Assert.ThrowsAsync<SessionError>(() => service.SetAsync("class",
            new SetSessionAttendancePolicyRequest { LateGraceMinutes = 20, AbsentAfterMinutes = 10 },
            Actor, "evt_0199e0c1-0000-7000-8000-000000000012", CancellationToken.None));

        Assert.Equal("validation_failed", error.Code);
    }

    [Fact]
    public async Task AC02_setting_a_policy_is_audited()
    {
        var service = BuildService(out var permissionGateway, out var auditSink);
        permissionGateway.GrantedActorIds.Add(Actor.Id);

        await service.SetAsync("hostel", new SetSessionAttendancePolicyRequest { LateGraceMinutes = 10, AbsentAfterMinutes = 20 },
            Actor, "evt_0199e0c1-0000-7000-8000-000000000013", CancellationToken.None);

        Assert.Contains(auditSink.All(), e => e.Action == "attendance_session_policy.updated");
    }
}
