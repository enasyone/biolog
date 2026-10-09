using Biolog.Features.WorkforceAttendance.Domain;
using Xunit;

namespace Biolog.Features.WorkforceAttendance.Tests;

/// <summary>BIO-ATT-003-F01 (High, Seyi round 1): lateness and absence interpretation, derived from a shift's own start time and grace period, never a raw-event mutation.</summary>
public sealed class AttendanceInterpretationEvaluatorTests
{
    private static WorkforceShift Shift(string startTime, string endTime, int graceMinutes) =>
        WorkforceShift.Create("ten_018f2e5a-1234-7abc-89ab-0123456789ab", "Day", startTime, endTime, graceMinutes, "per_018f2e5a-1234-7abc-89ab-0123456789ab", "2026-10-07T00:00:00.000Z");

    private static ClockEventView CheckIn(string eventId, string occurredAt) =>
        new() { EventId = eventId, Mode = ClockMode.CheckIn, OccurredAt = occurredAt };

    private static ClockEventView CheckOut(string eventId, string occurredAt) =>
        new() { EventId = eventId, Mode = ClockMode.CheckOut, OccurredAt = occurredAt };

    private static ClockEventView Presence(string eventId, string occurredAt) =>
        new() { EventId = eventId, Mode = ClockMode.Presence, OccurredAt = occurredAt };

    [Fact]
    public void No_events_at_all_is_absent()
    {
        var shift = Shift("08:00", "17:00", graceMinutes: 10);

        var result = AttendanceInterpretationEvaluator.Evaluate([], shift);

        Assert.Equal(AttendanceInterpretationStatus.Absent, result.Status);
        Assert.Null(result.FirstCheckInEventId);
        Assert.Null(result.MinutesLate);
    }

    [Fact]
    public void A_presence_only_day_is_still_absent_presence_is_not_a_check_in()
    {
        var shift = Shift("08:00", "17:00", graceMinutes: 10);
        var events = new[] { Presence("evt_p", "2026-10-07T08:00:00.000Z") };

        Assert.Equal(AttendanceInterpretationStatus.Absent, AttendanceInterpretationEvaluator.Evaluate(events, shift).Status);
    }

    [Fact]
    public void A_check_out_only_day_with_no_check_in_is_still_absent()
    {
        var shift = Shift("08:00", "17:00", graceMinutes: 10);
        var events = new[] { CheckOut("evt_out", "2026-10-07T08:00:00.000Z") };

        Assert.Equal(AttendanceInterpretationStatus.Absent, AttendanceInterpretationEvaluator.Evaluate(events, shift).Status);
    }

    [Fact]
    public void Arriving_exactly_at_the_shifts_start_time_is_on_time()
    {
        var shift = Shift("08:00", "17:00", graceMinutes: 10);
        var events = new[] { CheckIn("evt_in", "2026-10-07T08:00:00.000Z") };

        var result = AttendanceInterpretationEvaluator.Evaluate(events, shift);

        Assert.Equal(AttendanceInterpretationStatus.OnTime, result.Status);
        Assert.Equal("evt_in", result.FirstCheckInEventId);
        Assert.Null(result.MinutesLate);
    }

    [Fact]
    public void Arriving_exactly_at_the_grace_boundary_is_still_on_time()
    {
        var shift = Shift("08:00", "17:00", graceMinutes: 10);
        var events = new[] { CheckIn("evt_in", "2026-10-07T08:10:00.000Z") };

        Assert.Equal(AttendanceInterpretationStatus.OnTime, AttendanceInterpretationEvaluator.Evaluate(events, shift).Status);
    }

    [Fact]
    public void Reviewers_own_reproduction_08_11_arrival_against_08_00_plus_10_minute_grace_is_late_by_one_minute()
    {
        var shift = Shift("08:00", "17:00", graceMinutes: 10);
        var events = new[] { CheckIn("evt_in", "2026-10-07T08:11:00.000Z") };

        var result = AttendanceInterpretationEvaluator.Evaluate(events, shift);

        Assert.Equal(AttendanceInterpretationStatus.Late, result.Status);
        Assert.Equal("evt_in", result.FirstCheckInEventId);
        Assert.Equal(1, result.MinutesLate);
    }

    [Fact]
    public void SY_ATT003_03_regression_an_early_arrival_before_a_same_day_shift_is_on_time_not_wrapped_to_almost_a_full_day_late()
    {
        // Round-2 regression (SY-ATT003-03, High, Seyi round 2): an unconditional modulo-1440
        // wrapped this genuinely early arrival (-5 minutes relative to start+grace) to 1425
        // minutes "late" instead of recognising it as on time.
        var shift = Shift("08:00", "17:00", graceMinutes: 10);
        var events = new[] { CheckIn("evt_in", "2026-10-07T07:55:00.000Z") }; // 5 minutes early

        var result = AttendanceInterpretationEvaluator.Evaluate(events, shift);

        Assert.Equal(AttendanceInterpretationStatus.OnTime, result.Status);
        Assert.Null(result.MinutesLate);
    }

    [Fact]
    public void A_very_early_arrival_well_before_a_same_day_shift_is_still_on_time_never_wrapped()
    {
        var shift = Shift("08:00", "17:00", graceMinutes: 10);
        var events = new[] { CheckIn("evt_in", "2026-10-07T00:01:00.000Z") }; // 7h59m early

        Assert.Equal(AttendanceInterpretationStatus.OnTime, AttendanceInterpretationEvaluator.Evaluate(events, shift).Status);
    }

    [Fact]
    public void An_overnight_shifts_own_early_same_evening_arrival_is_on_time_not_wrapped_into_a_near_full_day_late_reading()
    {
        // Distinguishes "ten minutes before tonight's overnight start" from "the unwrapped
        // reading of an after-midnight arrival" - both have a negative raw delta; the
        // evaluator must pick the interpretation closer to zero (early), not the wrapped one.
        var shift = Shift("22:00", "06:00", graceMinutes: 10);
        var events = new[] { CheckIn("evt_in", "2026-10-07T21:50:00.000Z") }; // 10 minutes early

        var result = AttendanceInterpretationEvaluator.Evaluate(events, shift);

        Assert.Equal(AttendanceInterpretationStatus.OnTime, result.Status);
        Assert.Null(result.MinutesLate);
    }

    [Fact]
    public void An_overnight_shift_treats_an_early_next_day_arrival_as_minutes_after_the_wrapped_start()
    {
        var shift = Shift("22:00", "06:00", graceMinutes: 15); // wraps past midnight
        var events = new[] { CheckIn("evt_in", "2026-10-07T00:20:00.000Z") }; // 00:20, 140 minutes after 22:00

        var result = AttendanceInterpretationEvaluator.Evaluate(events, shift);

        Assert.Equal(AttendanceInterpretationStatus.Late, result.Status);
        Assert.Equal(125, result.MinutesLate); // 140 - 15 grace
    }

    [Fact]
    public void An_overnight_shift_arrival_within_grace_past_midnight_is_still_on_time()
    {
        var shift = Shift("23:50", "06:00", graceMinutes: 30); // threshold wraps to 00:20
        var events = new[] { CheckIn("evt_in", "2026-10-07T00:10:00.000Z") }; // 20 minutes after 23:50

        Assert.Equal(AttendanceInterpretationStatus.OnTime, AttendanceInterpretationEvaluator.Evaluate(events, shift).Status);
    }

    [Fact]
    public void An_open_check_in_with_no_check_out_still_gets_a_real_lateness_interpretation_not_absent()
    {
        var shift = Shift("08:00", "17:00", graceMinutes: 10);
        var events = new[] { CheckIn("evt_in", "2026-10-07T09:00:00.000Z") }; // 60 minutes after start, never clocked out

        var result = AttendanceInterpretationEvaluator.Evaluate(events, shift);

        Assert.Equal(AttendanceInterpretationStatus.Late, result.Status);
        Assert.Equal(50, result.MinutesLate);
    }

    [Fact]
    public void Only_the_first_chronological_check_in_of_the_day_is_judged_a_later_overlapping_check_in_is_ignored()
    {
        var shift = Shift("08:00", "17:00", graceMinutes: 10);
        var events = new[]
        {
            CheckIn("evt_in1", "2026-10-07T08:00:00.000Z"), // on time
            CheckIn("evt_in2", "2026-10-07T12:00:00.000Z"), // would be very late if judged instead
        };

        var result = AttendanceInterpretationEvaluator.Evaluate(events, shift);

        Assert.Equal(AttendanceInterpretationStatus.OnTime, result.Status);
        Assert.Equal("evt_in1", result.FirstCheckInEventId);
    }

    [Fact]
    public void Events_out_of_chronological_order_in_the_input_are_still_ordered_before_judging()
    {
        var shift = Shift("08:00", "17:00", graceMinutes: 10);
        var events = new[]
        {
            CheckIn("evt_in_later", "2026-10-07T12:00:00.000Z"),
            CheckIn("evt_in_earlier", "2026-10-07T08:00:00.000Z"),
        };

        Assert.Equal("evt_in_earlier", AttendanceInterpretationEvaluator.Evaluate(events, shift).FirstCheckInEventId);
    }
}
