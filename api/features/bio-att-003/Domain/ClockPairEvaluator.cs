using Biolog.Features.WorkforceAttendance.Identifiers;

namespace Biolog.Features.WorkforceAttendance.Domain;

/// <summary>
/// AC02 - the one place raw clock events become validated pairs. A pure function: the same set
/// of <see cref="ClockEventView"/>s always produces the same pairs, every time - no clock read,
/// no I/O, no mutation of the input, the same discipline BIO-ATT-001's own
/// <c>AttendanceStatusEvaluator</c> established for itself.
///
/// <see cref="ClockMode.Presence"/> events are ignored entirely here - they are BIO-ATT-001's
/// own single-tap attendance signal, not a clock-in/clock-out boundary; pairing them would
/// invent a meaning AC02 never asks for.
/// </summary>
public static class ClockPairEvaluator
{
    /// <summary><paramref name="dayEvents"/> must already be filtered to one (tenant, person, calendar date) - this method does not filter by date itself, the same caller contract <c>AttendanceStatusEvaluator.Evaluate</c> states for itself.</summary>
    public static IReadOnlyList<WorkforceClockPair> Evaluate(IReadOnlyList<ClockEventView> dayEvents)
    {
        var ordered = dayEvents
            .Where(e => e.Mode is ClockMode.CheckIn or ClockMode.CheckOut)
            .OrderBy(e => e.OccurredAt, StringComparer.Ordinal) // RFC 3339 UTC sorts lexicographically == chronologically
            .ToList();

        var pairs = new List<WorkforceClockPair>();
        ClockEventView? openCheckIn = null;

        foreach (var evt in ordered)
        {
            if (evt.Mode == ClockMode.CheckIn)
            {
                if (openCheckIn is not null)
                {
                    // A second CheckIn before the prior one was closed - never silently treated
                    // as closing it. The prior open CheckIn is surfaced as its own Overlapping
                    // finding, not merged into this one.
                    pairs.Add(new WorkforceClockPair { Status = ClockPairStatus.Overlapping, CheckInEventId = openCheckIn.EventId });
                }

                openCheckIn = evt;
                continue;
            }

            // evt.Mode == ClockMode.CheckOut
            if (openCheckIn is null)
            {
                pairs.Add(new WorkforceClockPair { Status = ClockPairStatus.UnmatchedCheckOut, CheckOutEventId = evt.EventId });
                continue;
            }

            var duration = DurationMinutes(openCheckIn.OccurredAt, evt.OccurredAt);
            pairs.Add(new WorkforceClockPair
            {
                Status = ClockPairStatus.Complete,
                CheckInEventId = openCheckIn.EventId,
                CheckOutEventId = evt.EventId,
                DurationMinutes = duration,
            });
            openCheckIn = null;
        }

        if (openCheckIn is not null)
        {
            pairs.Add(new WorkforceClockPair { Status = ClockPairStatus.OpenCheckIn, CheckInEventId = openCheckIn.EventId });
        }

        return pairs;
    }

    private static int DurationMinutes(string checkInOccurredAt, string checkOutOccurredAt)
    {
        UtcInstant.TryParse(checkInOccurredAt, out var checkIn);
        UtcInstant.TryParse(checkOutOccurredAt, out var checkOut);
        var minutes = (checkOut - checkIn).TotalMinutes;
        return (int)Math.Max(0, minutes);
    }
}
