using Biolog.Features.Attendance.Identifiers;

namespace Biolog.Features.Attendance.Domain;

/// <summary>
/// AC02/AC04 - the one place raw events become a derived status. A pure function: same
/// (events, policy) in, same <see cref="DerivedAttendanceStatus"/> out, every time - no clock
/// read, no I/O, no mutation of either input. Proving AC02's "raw is distinct from derived" and
/// AC04's "tenant policies define interpretation" both come down to this: the SAME set of raw
/// events classifies differently under two different policies, and no policy evaluation can
/// ever change what the raw events themselves say happened.
/// </summary>
public static class AttendanceStatusEvaluator
{
    /// <summary><paramref name="dayEvents"/> must already be filtered to one (tenant, person, calendar date) - this method does not filter by date itself, so a caller error there is a caller error, not something silently corrected here.</summary>
    public static DerivedAttendanceStatus Evaluate(string tenantId, string personId, string date, IReadOnlyList<AttendanceEvent> dayEvents, AttendancePolicy policy, string now)
    {
        var arrivals = dayEvents
            .Where(e => e.Mode is AttendanceMode.CheckIn or AttendanceMode.Presence)
            .OrderBy(e => e.OccurredAt, StringComparer.Ordinal) // RFC 3339 UTC sorts lexicographically == chronologically
            .ToList();

        if (arrivals.Count == 0)
        {
            return new DerivedAttendanceStatus
            {
                TenantId = tenantId,
                PersonId = personId,
                Date = date,
                Status = AttendanceStatusValue.Absent,
                BasedOnEventIds = [],
                PolicyVersion = policy.Version,
                ComputedAt = now,
            };
        }

        var first = arrivals[0];

        if (policy.ExpectedStartTime is null)
        {
            // No expected start time configured for this tenant - lateness cannot be judged
            // against nothing, so any arrival at all is simply present (AttendancePolicy.Default's
            // own documented behaviour).
            return new DerivedAttendanceStatus
            {
                TenantId = tenantId,
                PersonId = personId,
                Date = date,
                Status = AttendanceStatusValue.Present,
                BasedOnEventIds = [first.EventId],
                PolicyVersion = policy.Version,
                ComputedAt = now,
            };
        }

        UtcInstant.TryParse(first.OccurredAt, out var occurredAtInstant);
        var arrivalTime = TimeOnly.FromDateTime(occurredAtInstant);
        var expected = TimeOnly.ParseExact(policy.ExpectedStartTime, "HH:mm");
        var cutoff = expected.Add(TimeSpan.FromMinutes(policy.LateGraceMinutes));

        var status = arrivalTime <= cutoff ? AttendanceStatusValue.Present : AttendanceStatusValue.Late;

        return new DerivedAttendanceStatus
        {
            TenantId = tenantId,
            PersonId = personId,
            Date = date,
            Status = status,
            BasedOnEventIds = [first.EventId],
            PolicyVersion = policy.Version,
            ComputedAt = now,
        };
    }
}
