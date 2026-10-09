using Biolog.Features.TransportEvents.Identifiers;

namespace Biolog.Features.TransportEvents.Domain;

/// <summary>
/// AC03 - the one place raw board/alight events become a derived presence classification. A pure
/// function: same (events, now) in, same <see cref="DerivedTransportPresence"/> out, every time -
/// no I/O, no mutation of any input. Mirrors <c>AttendanceStatusEvaluator</c>'s own shape.
///
/// <b>Missing-alight exception window.</b> A Board with no later matching Alight in the same
/// day's events is <see cref="DerivedPresenceStatus.OnBoard"/> while still plausibly in progress
/// (the same calendar day, within <see cref="MissingAlightExceptionWindowHours"/> of boarding) and
/// becomes <see cref="DerivedPresenceStatus.MissingAlightException"/> once that window has passed,
/// or unconditionally for any date strictly before today - the classification of the SAME stored
/// events genuinely differs depending on WHEN it is read, proving AC03's "computed at read time,"
/// never a stored mutation.
/// </summary>
public static class TransportPresenceEvaluator
{
    public const int MissingAlightExceptionWindowHours = 12;

    /// <summary><paramref name="dayEvents"/> must already be filtered to one (tenant, person, calendar date) - this method does not filter by date itself.</summary>
    public static DerivedTransportPresence Evaluate(string tenantId, string personId, string date, IReadOnlyList<TransportEvent> dayEvents, string now)
    {
        if (dayEvents.Count == 0)
        {
            return new DerivedTransportPresence
            {
                TenantId = tenantId,
                PersonId = personId,
                Date = date,
                Status = DerivedPresenceStatus.NoActivity,
                BasedOnEventIds = [],
                ComputedAt = now,
            };
        }

        var ordered = dayEvents.OrderBy(e => e.OccurredAt, StringComparer.Ordinal).ToList(); // RFC 3339 UTC sorts lexicographically == chronologically

        TransportEvent? openBoard = null;
        TransportEvent? lastAlight = null;
        foreach (var evt in ordered)
        {
            if (evt.Mode == TransportEventMode.Board)
            {
                openBoard = evt;
            }
            else if (openBoard is not null)
            {
                lastAlight = evt;
                openBoard = null;
            }
            else
            {
                // An alight with no preceding open board this day - not this evaluator's concern
                // (AC03 is about a MISSING alight, not an orphan one); recorded but does not open
                // or close a board window.
                lastAlight = evt;
            }
        }

        if (openBoard is not null)
        {
            UtcInstant.TryParse(now, out var nowInstant);
            var isPastDate = string.CompareOrdinal(date, DateOnly.FromDateTime(nowInstant).ToString("yyyy-MM-dd")) < 0;

            UtcInstant.TryParse(openBoard.OccurredAt, out var boardedAtInstant);
            var isPastWindow = nowInstant - boardedAtInstant >= TimeSpan.FromHours(MissingAlightExceptionWindowHours);

            var status = isPastDate || isPastWindow ? DerivedPresenceStatus.MissingAlightException : DerivedPresenceStatus.OnBoard;
            return new DerivedTransportPresence
            {
                TenantId = tenantId,
                PersonId = personId,
                Date = date,
                Status = status,
                BasedOnEventIds = [openBoard.EventId],
                ComputedAt = now,
            };
        }

        return new DerivedTransportPresence
        {
            TenantId = tenantId,
            PersonId = personId,
            Date = date,
            Status = DerivedPresenceStatus.Alighted,
            BasedOnEventIds = lastAlight is null ? [] : [lastAlight.EventId],
            ComputedAt = now,
        };
    }
}
