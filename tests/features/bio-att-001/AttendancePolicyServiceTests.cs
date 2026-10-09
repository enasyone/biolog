using Biolog.Features.Attendance.Api;
using Biolog.Features.Attendance.Audit;
using Biolog.Features.Attendance.Domain;
using Xunit;

namespace Biolog.Features.Attendance.Tests;

/// <summary>AC04 (tenant policy) - the one authorization decision this feature makes (ADR-0006 Decision 1/2).</summary>
public class AttendancePolicyServiceTests
{
    private const string TenantId = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77";
    private static readonly AuditActor Actor = new(ActorType.User, "usr-admin-1");

    private static AttendancePolicyService MakeService(out InMemoryAttendancePolicyRepository repository, out InMemoryAttendanceAuditSink auditSink, IPermissionGateway? permissionGateway = null)
    {
        repository = new InMemoryAttendancePolicyRepository();
        auditSink = new InMemoryAttendanceAuditSink();
        return new AttendancePolicyService(
            new FakeCurrentTenantProvider(TenantId), repository, auditSink, new AttendancePolicyLockRegistry(), permissionGateway ?? new FakePermissionGateway());
    }

    [Fact]
    public async Task AC04_a_a_tenant_with_no_explicit_policy_still_has_an_effective_default()
    {
        var service = MakeService(out _, out _);
        var policy = await service.GetAsync();

        Assert.Equal(AttendanceInterpretationMode.Presence, policy.InterpretationMode);
        Assert.Null(policy.ExpectedStartTime);
        Assert.Equal(0, policy.Version);
    }

    [Fact]
    public async Task AC04_b_setting_a_policy_requires_the_manage_permission()
    {
        var denyingGateway = new FakePermissionGateway(new HashSet<(string, string)>()); // nobody holds anything
        var service = MakeService(out _, out _, denyingGateway);
        var request = new SetAttendancePolicyRequest { InterpretationMode = "education", ExpectedStartTime = "08:00", LateGraceMinutes = 10 };

        var ex = await Assert.ThrowsAsync<AttendanceError>(() => service.SetAsync(request, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f25"));
        Assert.Equal("permission_required", ex.Code);
        Assert.Equal(403, ex.Status);
    }

    [Fact]
    public async Task AC04_c_setting_a_policy_persists_it_and_advances_the_version()
    {
        var service = MakeService(out var repository, out var auditSink);
        var request = new SetAttendancePolicyRequest { InterpretationMode = "education", ExpectedStartTime = "08:00", LateGraceMinutes = 10 };

        var updated = await service.SetAsync(request, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f25");

        Assert.Equal(1, updated.Version);
        Assert.Equal(AttendanceInterpretationMode.Education, updated.InterpretationMode);
        var reread = await repository.GetAsync(TenantId);
        Assert.Equal(1, reread.Version);
        Assert.Single(auditSink.All());
        Assert.Equal("attendance.policy_updated", auditSink.All()[0].Action);
    }

    [Fact]
    public async Task AC04_d_a_second_write_advances_the_version_again_and_never_loses_the_first_writers_intent_without_recording_it()
    {
        var service = MakeService(out var repository, out _);
        await service.SetAsync(new SetAttendancePolicyRequest { InterpretationMode = "education", ExpectedStartTime = "08:00", LateGraceMinutes = 10 }, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f25");
        var second = await service.SetAsync(new SetAttendancePolicyRequest { InterpretationMode = "workforce", ExpectedStartTime = "09:00", LateGraceMinutes = 5 }, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f26");

        Assert.Equal(2, second.Version);
        Assert.Equal(AttendanceInterpretationMode.Workforce, second.InterpretationMode);
    }

    [Fact]
    public async Task AC04_e_invalid_expected_start_time_format_is_rejected()
    {
        var service = MakeService(out _, out _);
        var request = new SetAttendancePolicyRequest { InterpretationMode = "presence", ExpectedStartTime = "not-a-time", LateGraceMinutes = 0 };

        await Assert.ThrowsAsync<AttendanceError>(() => service.SetAsync(request, Actor, "evt_0199e0c2-3d46-72a7-be58-6a1b8c0d4f25"));
    }
}
