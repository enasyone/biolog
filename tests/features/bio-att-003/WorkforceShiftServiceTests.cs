using Biolog.Features.WorkforceAttendance.Api;
using Biolog.Features.WorkforceAttendance.Audit;
using Biolog.Features.WorkforceAttendance.Domain;
using Xunit;

namespace Biolog.Features.WorkforceAttendance.Tests;

/// <summary>AC01: shift schedules and grace periods are configurable.</summary>
public sealed class WorkforceShiftServiceTests
{
    private const string TenantId = "ten_018f2e5a-1234-7abc-89ab-0123456789ab";
    private static readonly AuditActor Actor = new(ActorType.User, "usr-admin-1");

    private static WorkforceShiftService MakeService(
        out InMemoryWorkforceShiftRepository repository, out InMemoryWorkforceAttendanceAuditSink auditSink, IPermissionGateway? permissionGateway = null)
    {
        repository = new InMemoryWorkforceShiftRepository();
        auditSink = new InMemoryWorkforceAttendanceAuditSink();
        return new WorkforceShiftService(new FakeCurrentTenantProvider(TenantId), repository, auditSink, new WorkforceShiftLockRegistry(), permissionGateway ?? new FakePermissionGateway());
    }

    [Fact]
    public async Task Creating_a_shift_persists_it_at_version_1_and_audits_it()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = MakeService(out var repository, out var auditSink);
        var request = new CreateWorkforceShiftRequest { Name = "Day", StartTime = "08:00", EndTime = "17:00", GraceMinutes = 10 };

        var shift = await service.CreateAsync(request, Actor, "evt_018f2e5a-9999-7abc-89ab-0123456789ab", ct);

        Assert.Equal(1, shift.Version);
        Assert.Equal("Day", shift.Name);
        Assert.Equal(540, shift.ExpectedDurationMinutes);
        var reread = await repository.GetAsync(TenantId, shift.ShiftId, ct);
        Assert.NotNull(reread);
        Assert.Single(auditSink.All());
        Assert.Equal("workforce_attendance.shift_created", auditSink.All()[0].Action);
    }

    [Fact]
    public async Task Creating_a_shift_requires_the_manage_permission()
    {
        var ct = TestContext.Current.CancellationToken;
        var denyingGateway = new FakePermissionGateway(new HashSet<(string, string)>());
        var service = MakeService(out _, out _, denyingGateway);
        var request = new CreateWorkforceShiftRequest { Name = "Day", StartTime = "08:00", EndTime = "17:00", GraceMinutes = 0 };

        var ex = await Assert.ThrowsAsync<WorkforceAttendanceError>(() => service.CreateAsync(request, Actor, "evt_018f2e5a-9999-7abc-89ab-0123456789ab", ct));

        Assert.Equal("permission_required", ex.Code);
        Assert.Equal(403, ex.Status);
    }

    [Theory]
    [InlineData("8am", "17:00")]
    [InlineData("08:00", "5pm")]
    public async Task An_invalid_time_format_is_rejected(string startTime, string endTime)
    {
        var ct = TestContext.Current.CancellationToken;
        var service = MakeService(out _, out _);
        var request = new CreateWorkforceShiftRequest { Name = "Day", StartTime = startTime, EndTime = endTime, GraceMinutes = 0 };

        var ex = await Assert.ThrowsAsync<WorkforceAttendanceError>(() => service.CreateAsync(request, Actor, "evt_018f2e5a-9999-7abc-89ab-0123456789ab", ct));

        Assert.Equal("validation_failed", ex.Code);
    }

    [Fact]
    public async Task Updating_a_shift_with_a_stale_version_is_rejected_and_does_not_mutate()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = MakeService(out var repository, out _);
        var shift = await service.CreateAsync(new CreateWorkforceShiftRequest { Name = "Day", StartTime = "08:00", EndTime = "17:00", GraceMinutes = 0 }, Actor, "evt_018f2e5a-0001-7abc-89ab-0123456789ab", ct);

        var ex = await Assert.ThrowsAsync<WorkforceAttendanceError>(() => service.UpdateAsync(
            shift.ShiftId,
            new UpdateWorkforceShiftRequest { Version = 0, Name = "Night", StartTime = "22:00", EndTime = "06:00", GraceMinutes = 5 },
            Actor, "evt_018f2e5a-0002-7abc-89ab-0123456789ab", ct));

        Assert.Equal("concurrency_conflict", ex.Code);
        var reread = await repository.GetAsync(TenantId, shift.ShiftId, ct);
        Assert.Equal("Day", reread!.Name);
    }

    [Fact]
    public async Task Updating_a_shift_with_the_correct_version_persists_and_advances_the_version()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = MakeService(out var repository, out var auditSink);
        var shift = await service.CreateAsync(new CreateWorkforceShiftRequest { Name = "Day", StartTime = "08:00", EndTime = "17:00", GraceMinutes = 0 }, Actor, "evt_018f2e5a-0001-7abc-89ab-0123456789ab", ct);

        var updated = await service.UpdateAsync(
            shift.ShiftId,
            new UpdateWorkforceShiftRequest { Version = 1, Name = "Day (revised)", StartTime = "08:30", EndTime = "17:30", GraceMinutes = 15 },
            Actor, "evt_018f2e5a-0002-7abc-89ab-0123456789ab", ct);

        Assert.Equal(2, updated.Version);
        Assert.Equal("Day (revised)", updated.Name);
        var reread = await repository.GetAsync(TenantId, shift.ShiftId, ct);
        Assert.Equal(2, reread!.Version);
        Assert.Equal(2, auditSink.All().Count);
    }

    [Fact]
    public async Task Updating_a_nonexistent_shift_is_rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = MakeService(out _, out _);

        var ex = await Assert.ThrowsAsync<WorkforceAttendanceError>(() => service.UpdateAsync(
            "evt_018f2e5a-dead-7abc-89ab-0123456789ab",
            new UpdateWorkforceShiftRequest { Version = 0, Name = "Night", StartTime = "22:00", EndTime = "06:00", GraceMinutes = 5 },
            Actor, "evt_018f2e5a-0002-7abc-89ab-0123456789ab", ct));

        Assert.Equal("shift_not_found", ex.Code);
    }

    [Fact]
    public async Task Reading_shifts_requires_no_permission()
    {
        var ct = TestContext.Current.CancellationToken;
        var denyingGateway = new FakePermissionGateway(new HashSet<(string, string)>());
        var service = MakeService(out _, out _, denyingGateway);

        var shifts = await service.ListAsync(ct);

        Assert.Empty(shifts); // no throw - reads stay open
    }

    [Fact]
    public async Task Two_shifts_for_the_same_tenant_are_both_listed_independently()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = MakeService(out _, out _);
        await service.CreateAsync(new CreateWorkforceShiftRequest { Name = "Day", StartTime = "08:00", EndTime = "17:00", GraceMinutes = 0 }, Actor, "evt_018f2e5a-0001-7abc-89ab-0123456789ab", ct);
        await service.CreateAsync(new CreateWorkforceShiftRequest { Name = "Night", StartTime = "22:00", EndTime = "06:00", GraceMinutes = 0 }, Actor, "evt_018f2e5a-0002-7abc-89ab-0123456789ab", ct);

        var shifts = await service.ListAsync(ct);

        Assert.Equal(2, shifts.Count);
    }
}
