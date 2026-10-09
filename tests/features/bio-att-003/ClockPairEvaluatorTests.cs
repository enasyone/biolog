using Biolog.Features.WorkforceAttendance.Domain;
using Xunit;

namespace Biolog.Features.WorkforceAttendance.Tests;

/// <summary>AC02: clock pairs are validated, never silently guessed.</summary>
public sealed class ClockPairEvaluatorTests
{
    private static ClockEventView Evt(string id, ClockMode mode, string occurredAt) =>
        new() { EventId = id, Mode = mode, OccurredAt = occurredAt };

    [Fact]
    public void A_check_in_followed_by_a_check_out_is_complete_with_the_right_duration()
    {
        var events = new[]
        {
            Evt("evt_in", ClockMode.CheckIn, "2026-10-07T08:00:00.000Z"),
            Evt("evt_out", ClockMode.CheckOut, "2026-10-07T17:00:00.000Z"),
        };

        var pairs = ClockPairEvaluator.Evaluate(events);

        Assert.Single(pairs);
        Assert.Equal(ClockPairStatus.Complete, pairs[0].Status);
        Assert.Equal("evt_in", pairs[0].CheckInEventId);
        Assert.Equal("evt_out", pairs[0].CheckOutEventId);
        Assert.Equal(540, pairs[0].DurationMinutes);
    }

    [Fact]
    public void A_check_in_with_no_check_out_is_open()
    {
        var events = new[] { Evt("evt_in", ClockMode.CheckIn, "2026-10-07T08:00:00.000Z") };

        var pairs = ClockPairEvaluator.Evaluate(events);

        Assert.Single(pairs);
        Assert.Equal(ClockPairStatus.OpenCheckIn, pairs[0].Status);
        Assert.Equal("evt_in", pairs[0].CheckInEventId);
        Assert.Null(pairs[0].CheckOutEventId);
        Assert.Null(pairs[0].DurationMinutes);
    }

    [Fact]
    public void A_check_out_with_no_preceding_check_in_is_unmatched()
    {
        var events = new[] { Evt("evt_out", ClockMode.CheckOut, "2026-10-07T17:00:00.000Z") };

        var pairs = ClockPairEvaluator.Evaluate(events);

        Assert.Single(pairs);
        Assert.Equal(ClockPairStatus.UnmatchedCheckOut, pairs[0].Status);
        Assert.Null(pairs[0].CheckInEventId);
        Assert.Equal("evt_out", pairs[0].CheckOutEventId);
    }

    [Fact]
    public void A_second_check_in_before_the_first_is_closed_is_overlapping_never_silently_closes_the_prior_one()
    {
        var events = new[]
        {
            Evt("evt_in1", ClockMode.CheckIn, "2026-10-07T08:00:00.000Z"),
            Evt("evt_in2", ClockMode.CheckIn, "2026-10-07T09:00:00.000Z"),
            Evt("evt_out2", ClockMode.CheckOut, "2026-10-07T17:00:00.000Z"),
        };

        var pairs = ClockPairEvaluator.Evaluate(events);

        Assert.Equal(2, pairs.Count);
        Assert.Equal(ClockPairStatus.Overlapping, pairs[0].Status);
        Assert.Equal("evt_in1", pairs[0].CheckInEventId);
        Assert.Equal(ClockPairStatus.Complete, pairs[1].Status);
        Assert.Equal("evt_in2", pairs[1].CheckInEventId);
        Assert.Equal("evt_out2", pairs[1].CheckOutEventId);
    }

    [Fact]
    public void Two_complete_pairs_in_one_day_are_both_reported()
    {
        var events = new[]
        {
            Evt("evt_in1", ClockMode.CheckIn, "2026-10-07T08:00:00.000Z"),
            Evt("evt_out1", ClockMode.CheckOut, "2026-10-07T12:00:00.000Z"),
            Evt("evt_in2", ClockMode.CheckIn, "2026-10-07T13:00:00.000Z"),
            Evt("evt_out2", ClockMode.CheckOut, "2026-10-07T17:00:00.000Z"),
        };

        var pairs = ClockPairEvaluator.Evaluate(events);

        Assert.Equal(2, pairs.Count);
        Assert.All(pairs, p => Assert.Equal(ClockPairStatus.Complete, p.Status));
        Assert.Equal(240, pairs[0].DurationMinutes);
        Assert.Equal(240, pairs[1].DurationMinutes);
    }

    [Fact]
    public void Events_out_of_chronological_order_in_the_input_are_still_paired_correctly()
    {
        var events = new[]
        {
            Evt("evt_out", ClockMode.CheckOut, "2026-10-07T17:00:00.000Z"),
            Evt("evt_in", ClockMode.CheckIn, "2026-10-07T08:00:00.000Z"),
        };

        var pairs = ClockPairEvaluator.Evaluate(events);

        Assert.Single(pairs);
        Assert.Equal(ClockPairStatus.Complete, pairs[0].Status);
    }

    [Fact]
    public void Presence_events_are_ignored_entirely_never_paired()
    {
        var events = new[] { Evt("evt_p", ClockMode.Presence, "2026-10-07T08:00:00.000Z") };

        var pairs = ClockPairEvaluator.Evaluate(events);

        Assert.Empty(pairs);
    }

    [Fact]
    public void No_events_produces_no_pairs()
    {
        var pairs = ClockPairEvaluator.Evaluate([]);

        Assert.Empty(pairs);
    }
}
