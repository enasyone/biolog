using Biolog.Features.TransportEvents.Domain;
using Xunit;

namespace Biolog.Features.TransportEvents.Tests;

/// <summary>AC03 - missing-alight/exception states, computed at read time only.</summary>
public class TransportPresenceEvaluatorTests
{
    private const string TenantId = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77";
    private const string PersonId = "per_0199e0c2-1111-7222-8333-444455556666";
    private const string Date = "2026-01-01";

    private static TransportEvent MakeEvent(TransportEventMode mode, string occurredAt) => TransportEvent.Capture(
        TenantId, PersonId, "route-A", "bus-01", "stop-1", null, mode,
        [new TransportEvidenceEntry { Method = EvidenceMethod.Uhf, Verified = true, Confidence = 0.9 }],
        occurredAt, occurredAt, $"idem-{Guid.CreateVersion7()}");

    [Fact]
    public void AC03_a_no_events_at_all_is_NoActivity()
    {
        var result = TransportPresenceEvaluator.Evaluate(TenantId, PersonId, Date, [], now: "2026-01-01T09:00:00.000Z");
        Assert.Equal(DerivedPresenceStatus.NoActivity, result.Status);
        Assert.Empty(result.BasedOnEventIds);
    }

    [Fact]
    public void AC03_b_a_recent_unmatched_board_is_OnBoard()
    {
        var board = MakeEvent(TransportEventMode.Board, "2026-01-01T08:00:00.000Z");

        var result = TransportPresenceEvaluator.Evaluate(TenantId, PersonId, Date, [board], now: "2026-01-01T08:30:00.000Z"); // 30 minutes later

        Assert.Equal(DerivedPresenceStatus.OnBoard, result.Status);
        Assert.Equal([board.EventId], result.BasedOnEventIds);
    }

    [Fact]
    public void AC03_c_a_board_followed_by_a_later_alight_is_Alighted()
    {
        var board = MakeEvent(TransportEventMode.Board, "2026-01-01T08:00:00.000Z");
        var alight = MakeEvent(TransportEventMode.Alight, "2026-01-01T08:20:00.000Z");

        var result = TransportPresenceEvaluator.Evaluate(TenantId, PersonId, Date, [board, alight], now: "2026-01-01T09:00:00.000Z");

        Assert.Equal(DerivedPresenceStatus.Alighted, result.Status);
        Assert.Equal([alight.EventId], result.BasedOnEventIds);
    }

    [Fact]
    public void AC03_d_events_out_of_insertion_order_are_still_evaluated_chronologically()
    {
        var board = MakeEvent(TransportEventMode.Board, "2026-01-01T08:00:00.000Z");
        var alight = MakeEvent(TransportEventMode.Alight, "2026-01-01T08:20:00.000Z");

        var result = TransportPresenceEvaluator.Evaluate(TenantId, PersonId, Date, [alight, board], now: "2026-01-01T09:00:00.000Z"); // alight listed first

        Assert.Equal(DerivedPresenceStatus.Alighted, result.Status);
    }

    [Fact]
    public void AC03_e_an_unmatched_board_past_the_exception_window_the_same_day_is_MissingAlightException()
    {
        var board = MakeEvent(TransportEventMode.Board, "2026-01-01T08:00:00.000Z");
        var now = "2026-01-01T21:00:01.000Z"; // 13 hours later, past the 12-hour window

        var result = TransportPresenceEvaluator.Evaluate(TenantId, PersonId, Date, [board], now);

        Assert.Equal(DerivedPresenceStatus.MissingAlightException, result.Status);
    }

    [Fact]
    public void AC03_f_an_unmatched_board_from_a_date_strictly_before_today_is_MissingAlightException_regardless_of_the_window()
    {
        var board = MakeEvent(TransportEventMode.Board, "2026-01-01T08:00:00.000Z");

        // Only ~16 minutes after boarding (well inside the exception window), but now's own date is
        // 2026-01-02, so the evaluated date (2026-01-01) is already in the past.
        var result = TransportPresenceEvaluator.Evaluate(TenantId, PersonId, Date, [board], now: "2026-01-02T00:00:01.000Z");

        Assert.Equal(DerivedPresenceStatus.MissingAlightException, result.Status);
    }

    [Fact]
    public void AC03_g_the_IDENTICAL_stored_events_classify_differently_at_two_different_read_times_proving_computed_at_read_time()
    {
        // The core AC03 proof: same stored data, two different "now"s, two different answers -
        // nothing about the events themselves ever changes.
        var board = MakeEvent(TransportEventMode.Board, "2026-01-01T08:00:00.000Z");
        var events = new[] { board };

        var early = TransportPresenceEvaluator.Evaluate(TenantId, PersonId, Date, events, now: "2026-01-01T08:30:00.000Z");
        var late = TransportPresenceEvaluator.Evaluate(TenantId, PersonId, Date, events, now: "2026-01-01T21:00:01.000Z");

        Assert.Equal(DerivedPresenceStatus.OnBoard, early.Status);
        Assert.Equal(DerivedPresenceStatus.MissingAlightException, late.Status);
    }

    [Fact]
    public void AC03_h_a_second_board_after_an_already_matched_alight_reopens_an_unmatched_board_window()
    {
        var firstBoard = MakeEvent(TransportEventMode.Board, "2026-01-01T08:00:00.000Z");
        var firstAlight = MakeEvent(TransportEventMode.Alight, "2026-01-01T08:20:00.000Z");
        var secondBoard = MakeEvent(TransportEventMode.Board, "2026-01-01T12:00:00.000Z");

        var result = TransportPresenceEvaluator.Evaluate(TenantId, PersonId, Date, [firstBoard, firstAlight, secondBoard], now: "2026-01-01T12:10:00.000Z");

        Assert.Equal(DerivedPresenceStatus.OnBoard, result.Status);
        Assert.Equal([secondBoard.EventId], result.BasedOnEventIds);
    }
}
