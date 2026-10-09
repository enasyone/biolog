using Biolog.Features.WorkforceAttendance.Api;
using Biolog.Features.WorkforceAttendance.Audit;
using Biolog.Features.WorkforceAttendance.Domain;
using Xunit;

namespace Biolog.Features.WorkforceAttendance.Tests;

/// <summary>AC02 (clock pairs are validated), AC03 (overtime is derived), AC04 (exceptions require audit reason) - end to end through the service.</summary>
public sealed class WorkforceAttendanceServiceTests
{
    private const string TenantId = "ten_018f2e5a-1234-7abc-89ab-0123456789ab";
    private const string PersonId = "per_018f2e5a-5678-7abc-89ab-0123456789ab";
    private const string Date = "2026-10-07";
    private static readonly AuditActor Actor = new(ActorType.User, "usr-admin-1");

    private static (WorkforceAttendanceService Service, WorkforceShiftService ShiftService, InMemoryWorkforceAttendanceAuditSink AuditSink, FakeAttendanceEventGateway EventGateway) MakeService(
        IPermissionGateway? permissionGateway = null, IPersonExistenceGateway? personGateway = null)
    {
        var shiftRepository = new InMemoryWorkforceShiftRepository();
        var adjustmentRepository = new InMemoryWorkforceAttendanceAdjustmentRepository();
        var auditSink = new InMemoryWorkforceAttendanceAuditSink();
        var eventGateway = new FakeAttendanceEventGateway();
        var grantAll = permissionGateway ?? new FakePermissionGateway();

        var shiftService = new WorkforceShiftService(new FakeCurrentTenantProvider(TenantId), shiftRepository, auditSink, new WorkforceShiftLockRegistry(), grantAll);
        var service = new WorkforceAttendanceService(
            new FakeCurrentTenantProvider(TenantId), eventGateway, shiftRepository, adjustmentRepository, auditSink, grantAll, personGateway ?? new FakePersonExistenceGateway());

        return (service, shiftService, auditSink, eventGateway);
    }

    private static async Task<string> CreateShiftAsync(WorkforceShiftService shiftService, CancellationToken ct) =>
        (await shiftService.CreateAsync(new CreateWorkforceShiftRequest { Name = "Day", StartTime = "08:00", EndTime = "17:00", GraceMinutes = 0 }, Actor, "evt_018f2e5a-0001-7abc-89ab-0123456789ab", ct)).ShiftId;

    [Fact]
    public async Task A_complete_clock_pair_within_the_shift_produces_zero_computed_overtime_and_no_adjustment()
    {
        var ct = TestContext.Current.CancellationToken;
        var (service, shiftService, _, eventGateway) = MakeService();
        var shiftId = await CreateShiftAsync(shiftService, ct);
        eventGateway.Add(PersonId, Date, new ClockEventView { EventId = "evt_in", Mode = ClockMode.CheckIn, OccurredAt = "2026-10-07T08:00:00.000Z" });
        eventGateway.Add(PersonId, Date, new ClockEventView { EventId = "evt_out", Mode = ClockMode.CheckOut, OccurredAt = "2026-10-07T17:00:00.000Z" });

        var summary = await service.GetDailySummaryAsync(PersonId, Date, shiftId, ct);

        Assert.Single(summary.Pairs);
        Assert.Equal(ClockPairStatus.Complete, summary.Pairs[0].Status);
        Assert.Equal(0, summary.ComputedOvertimeMinutes);
        Assert.Equal(0, summary.EffectiveOvertimeMinutes);
        Assert.Null(summary.Adjustment);
        Assert.Equal(AttendanceInterpretationStatus.OnTime, summary.Interpretation.Status);
    }

    [Fact]
    public async Task A_day_with_no_clock_events_is_interpreted_as_absent_through_the_real_service_not_just_the_pure_evaluator()
    {
        var ct = TestContext.Current.CancellationToken;
        var (service, shiftService, _, _) = MakeService();
        var shiftId = await CreateShiftAsync(shiftService, ct);

        var summary = await service.GetDailySummaryAsync(PersonId, Date, shiftId, ct);

        Assert.Empty(summary.Pairs);
        Assert.Equal(AttendanceInterpretationStatus.Absent, summary.Interpretation.Status);
        Assert.Null(summary.Interpretation.FirstCheckInEventId);
    }

    [Fact]
    public async Task A_late_arrival_is_interpreted_through_the_real_service_using_the_shifts_own_grace_period()
    {
        var ct = TestContext.Current.CancellationToken;
        var (service, shiftService, _, eventGateway) = MakeService();
        var shiftId = (await shiftService.CreateAsync(
            new CreateWorkforceShiftRequest { Name = "Day", StartTime = "08:00", EndTime = "17:00", GraceMinutes = 10 },
            Actor, "evt_018f2e5a-0002-7abc-89ab-0123456789ab", ct)).ShiftId;
        eventGateway.Add(PersonId, Date, new ClockEventView { EventId = "evt_in", Mode = ClockMode.CheckIn, OccurredAt = "2026-10-07T08:11:00.000Z" });

        var summary = await service.GetDailySummaryAsync(PersonId, Date, shiftId, ct);

        Assert.Equal(AttendanceInterpretationStatus.Late, summary.Interpretation.Status);
        Assert.Equal(1, summary.Interpretation.MinutesLate);
        Assert.Equal("evt_in", summary.Interpretation.FirstCheckInEventId);
    }

    [Fact]
    public async Task Working_past_the_shift_produces_positive_computed_overtime()
    {
        var ct = TestContext.Current.CancellationToken;
        var (service, shiftService, _, eventGateway) = MakeService();
        var shiftId = await CreateShiftAsync(shiftService, ct);
        eventGateway.Add(PersonId, Date, new ClockEventView { EventId = "evt_in", Mode = ClockMode.CheckIn, OccurredAt = "2026-10-07T08:00:00.000Z" });
        eventGateway.Add(PersonId, Date, new ClockEventView { EventId = "evt_out", Mode = ClockMode.CheckOut, OccurredAt = "2026-10-07T18:00:00.000Z" });

        var summary = await service.GetDailySummaryAsync(PersonId, Date, shiftId, ct);

        Assert.Equal(60, summary.ComputedOvertimeMinutes);
    }

    [Fact]
    public async Task A_summary_for_an_unknown_shift_is_rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        var (service, _, _, _) = MakeService();

        var ex = await Assert.ThrowsAsync<WorkforceAttendanceError>(() => service.GetDailySummaryAsync(PersonId, Date, "evt_018f2e5a-dead-7abc-89ab-0123456789ab", ct));

        Assert.Equal("shift_not_found", ex.Code);
    }

    [Fact]
    public async Task A_summary_for_a_nonexistent_person_is_rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        var (service, shiftService, _, _) = MakeService(personGateway: new FakePersonExistenceGateway(new HashSet<string>()));
        var shiftId = await CreateShiftAsync(shiftService, ct);

        var ex = await Assert.ThrowsAsync<WorkforceAttendanceError>(() => service.GetDailySummaryAsync(PersonId, Date, shiftId, ct));

        Assert.Equal("person_not_found", ex.Code);
    }

    [Fact]
    public async Task Creating_an_adjustment_requires_a_non_empty_reason()
    {
        var ct = TestContext.Current.CancellationToken;
        var (service, shiftService, _, _) = MakeService();
        var shiftId = await CreateShiftAsync(shiftService, ct);

        var ex = await Assert.ThrowsAsync<WorkforceAttendanceError>(() => service.CreateAdjustmentAsync(
            PersonId, Date, new CreateWorkforceAttendanceAdjustmentRequest { ShiftId = shiftId, AdjustedOvertimeMinutes = 30, Reason = "   " }, Actor, "evt_018f2e5a-0003-7abc-89ab-0123456789ab", ct));

        Assert.Equal("validation_failed", ex.Code);
    }

    [Fact]
    public async Task Creating_an_adjustment_requires_the_manage_permission()
    {
        var ct = TestContext.Current.CancellationToken;
        // Grants shift creation (needed to set up the test) but denies the adjustment-specific permission.
        var mixedGateway = new FakePermissionGateway(new HashSet<(string, string)> { (Actor.Id, WorkforceShiftService.ManagePermission) });
        var (service, shiftService, _, _) = MakeService(mixedGateway);
        var shiftId = await CreateShiftAsync(shiftService, ct);

        var ex = await Assert.ThrowsAsync<WorkforceAttendanceError>(() => service.CreateAdjustmentAsync(
            PersonId, Date, new CreateWorkforceAttendanceAdjustmentRequest { ShiftId = shiftId, AdjustedOvertimeMinutes = 30, Reason = "manual correction" }, Actor, "evt_018f2e5a-0003-7abc-89ab-0123456789ab", ct));

        Assert.Equal("permission_required", ex.Code);
    }

    [Fact]
    public async Task An_adjustment_overrides_the_effective_overtime_but_never_the_computed_value_and_is_audited()
    {
        var ct = TestContext.Current.CancellationToken;
        var (service, shiftService, auditSink, eventGateway) = MakeService();
        var shiftId = await CreateShiftAsync(shiftService, ct);
        eventGateway.Add(PersonId, Date, new ClockEventView { EventId = "evt_in", Mode = ClockMode.CheckIn, OccurredAt = "2026-10-07T08:00:00.000Z" });
        eventGateway.Add(PersonId, Date, new ClockEventView { EventId = "evt_out", Mode = ClockMode.CheckOut, OccurredAt = "2026-10-07T17:00:00.000Z" });

        var adjustment = await service.CreateAdjustmentAsync(
            PersonId, Date, new CreateWorkforceAttendanceAdjustmentRequest { ShiftId = shiftId, AdjustedOvertimeMinutes = 45, Reason = "device clock drift corrected manually" },
            Actor, "evt_018f2e5a-0003-7abc-89ab-0123456789ab", ct);

        Assert.Equal(0, adjustment.OriginalOvertimeMinutes);
        Assert.Equal(45, adjustment.AdjustedOvertimeMinutes);
        Assert.Equal("device clock drift corrected manually", adjustment.Reason);

        var summary = await service.GetDailySummaryAsync(PersonId, Date, shiftId, ct);
        Assert.Equal(0, summary.ComputedOvertimeMinutes); // the original derived value is still visible, never overwritten
        Assert.Equal(45, summary.EffectiveOvertimeMinutes); // the adjustment is what a caller should act on
        Assert.NotNull(summary.Adjustment);
        Assert.Equal(45, summary.Adjustment!.AdjustedOvertimeMinutes);

        Assert.Contains(auditSink.All(), e => e.Action == "workforce_attendance.adjustment_created");
    }

    [Fact]
    public async Task A_later_adjustment_for_the_same_day_replaces_which_one_is_effective_but_never_deletes_the_earlier_record()
    {
        var ct = TestContext.Current.CancellationToken;
        var (service, shiftService, _, eventGateway) = MakeService();
        var shiftId = await CreateShiftAsync(shiftService, ct);
        eventGateway.Add(PersonId, Date, new ClockEventView { EventId = "evt_in", Mode = ClockMode.CheckIn, OccurredAt = "2026-10-07T08:00:00.000Z" });
        eventGateway.Add(PersonId, Date, new ClockEventView { EventId = "evt_out", Mode = ClockMode.CheckOut, OccurredAt = "2026-10-07T17:00:00.000Z" });

        await service.CreateAdjustmentAsync(PersonId, Date, new CreateWorkforceAttendanceAdjustmentRequest { ShiftId = shiftId, AdjustedOvertimeMinutes = 30, Reason = "first correction" }, Actor, "evt_018f2e5a-0003-7abc-89ab-0123456789ab", ct);
        var second = await service.CreateAdjustmentAsync(PersonId, Date, new CreateWorkforceAttendanceAdjustmentRequest { ShiftId = shiftId, AdjustedOvertimeMinutes = 60, Reason = "second, more accurate correction" }, Actor, "evt_018f2e5a-0004-7abc-89ab-0123456789ab", ct);

        var summary = await service.GetDailySummaryAsync(PersonId, Date, shiftId, ct);

        Assert.Equal(60, summary.EffectiveOvertimeMinutes);
        Assert.Equal(second.AdjustmentId, summary.Adjustment!.AdjustmentId);
    }
}
