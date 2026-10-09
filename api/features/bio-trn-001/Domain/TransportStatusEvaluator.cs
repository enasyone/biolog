using Biolog.Features.Transport.Identifiers;

namespace Biolog.Features.Transport.Domain;

/// <summary>
/// AC02's own read-time overlay: a pure function, <c>(assignment, now) -&gt; EffectiveAssignmentStatus</c>.
/// No repository, no audit sink, no I/O - deliberately, so a reviewer can see at the type level
/// that this cannot mutate anything (the same discipline <c>VisitorStatusEvaluator</c>/
/// <c>AttendanceStatusEvaluator</c> already establish).
/// </summary>
public static class TransportStatusEvaluator
{
    public static EffectiveAssignmentStatus EffectiveStatus(PersonAssignment assignment, string now)
    {
        if (!UtcInstant.TryParse(now, out var nowInstant) || !UtcInstant.TryParse(assignment.ValidFrom, out var validFrom))
        {
            throw new TransportError("assignment.valid_from and 'now' must both be well-formed RFC 3339 UTC instants", "invalid_instant", 500);
        }

        if (assignment.ValidUntil is not null)
        {
            if (!UtcInstant.TryParse(assignment.ValidUntil, out var validUntil))
            {
                throw new TransportError("assignment.valid_until must be a well-formed RFC 3339 UTC instant", "invalid_instant", 500);
            }

            if (nowInstant >= validUntil)
            {
                return EffectiveAssignmentStatus.Ended;
            }
        }

        return nowInstant < validFrom ? EffectiveAssignmentStatus.Upcoming : EffectiveAssignmentStatus.Active;
    }
}
