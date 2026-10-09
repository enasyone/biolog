using Biolog.Features.WorkforceAttendance.Domain;
using Xunit;

namespace Biolog.Features.WorkforceAttendance.Tests;

/// <summary>AC03: overtime is derived, never a raw-event mutation.</summary>
public sealed class OvertimeEvaluatorTests
{
    private static WorkforceShift Shift(string startTime, string endTime) =>
        WorkforceShift.Create("ten_018f2e5a-1234-7abc-89ab-0123456789ab", "Day", startTime, endTime, graceMinutes: 0, "per_018f2e5a-1234-7abc-89ab-0123456789ab", "2026-10-07T00:00:00.000Z");

    private static WorkforceClockPair Complete(int durationMinutes) =>
        new() { Status = ClockPairStatus.Complete, CheckInEventId = "evt_in", CheckOutEventId = "evt_out", DurationMinutes = durationMinutes };

    [Fact]
    public void Working_exactly_the_shifts_duration_produces_zero_overtime()
    {
        var shift = Shift("08:00", "17:00"); // 540 minutes
        var pairs = new[] { Complete(540) };

        Assert.Equal(0, OvertimeEvaluator.EvaluateMinutes(pairs, shift));
    }

    [Fact]
    public void Working_longer_than_the_shift_produces_the_exact_excess_as_overtime()
    {
        var shift = Shift("08:00", "17:00"); // 540 minutes
        var pairs = new[] { Complete(600) };

        Assert.Equal(60, OvertimeEvaluator.EvaluateMinutes(pairs, shift));
    }

    [Fact]
    public void Working_shorter_than_the_shift_floors_at_zero_never_negative()
    {
        var shift = Shift("08:00", "17:00"); // 540 minutes
        var pairs = new[] { Complete(300) };

        Assert.Equal(0, OvertimeEvaluator.EvaluateMinutes(pairs, shift));
    }

    [Fact]
    public void Multiple_complete_pairs_sum_their_worked_minutes_before_comparing_to_the_shift()
    {
        var shift = Shift("08:00", "17:00"); // 540 minutes
        var pairs = new[] { Complete(300), Complete(300) }; // 600 total

        Assert.Equal(60, OvertimeEvaluator.EvaluateMinutes(pairs, shift));
    }

    [Fact]
    public void Irregular_pairs_contribute_no_duration_never_guessed()
    {
        var shift = Shift("08:00", "17:00");
        var pairs = new WorkforceClockPair[]
        {
            new() { Status = ClockPairStatus.OpenCheckIn, CheckInEventId = "evt_in" },
            new() { Status = ClockPairStatus.UnmatchedCheckOut, CheckOutEventId = "evt_out" },
            new() { Status = ClockPairStatus.Overlapping, CheckInEventId = "evt_in2" },
        };

        Assert.Equal(0, OvertimeEvaluator.EvaluateMinutes(pairs, shift));
    }

    [Fact]
    public void An_overnight_shift_wraps_past_midnight_for_its_own_expected_duration()
    {
        var shift = Shift("22:00", "06:00"); // wraps: 480 minutes
        Assert.Equal(480, shift.ExpectedDurationMinutes);

        var pairs = new[] { Complete(500) };
        Assert.Equal(20, OvertimeEvaluator.EvaluateMinutes(pairs, shift));
    }
}
