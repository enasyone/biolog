using Biolog.Features.AttendanceSessions.Identifiers;

namespace Biolog.Features.AttendanceSessions.Domain;

/// <summary>
/// AC01/AC02/AC03 - the one place a roster member's raw attendance evidence becomes a derived
/// present/late/absent classification for a specific <see cref="Session"/>. A pure function: same
/// (session, policy, evidence) in, same <see cref="SessionMemberClassification"/> out, every time -
/// no clock read, no I/O, no mutation of any input, the identical discipline BIO-ATT-001's own
/// <c>AttendanceStatusEvaluator</c> and BIO-TRN-002's own <c>TransportPresenceEvaluator</c> both
/// establish for exactly this reason: the SAME evidence classifies differently under two different
/// policies, and no policy evaluation can ever change what the raw evidence itself says happened.
///
/// Only <see cref="AttendanceEvidenceRecord.Mode"/> values <c>"check_in"</c>/<c>"presence"</c>
/// count as an arrival - the identical filter BIO-ATT-001's own evaluator applies to its own
/// <c>AttendanceMode.CheckIn</c>/<c>AttendanceMode.Presence</c>, applied here to this feature's own
/// flat wire-string projection of that same enum (this project cannot see BIO-ATT-001's own type -
/// no <c>ProjectReference</c>, see <see cref="AttendanceEvidenceRecord"/>'s header).
/// </summary>
public static class SessionAttendanceClassifier
{
    private const string CheckInMode = "check_in";
    private const string PresenceMode = "presence";

    public static SessionMemberClassification Evaluate(
        string tenantId, Session session, string personId, IReadOnlyList<AttendanceEvidenceRecord> personEvidence, SessionAttendancePolicy policy, string now)
    {
        var arrivals = personEvidence
            .Where(e => e.Mode is CheckInMode or PresenceMode)
            .OrderBy(e => e.OccurredAt, StringComparer.Ordinal) // RFC 3339 UTC sorts lexicographically == chronologically
            .ToList();

        if (arrivals.Count == 0)
        {
            return new SessionMemberClassification
            {
                TenantId = tenantId,
                SessionId = session.SessionId,
                PersonId = personId,
                Status = SessionAttendanceStatusValue.Absent,
                GroundingEvidence = [],
                PolicyVersion = policy.Version,
                ComputedAt = now,
            };
        }

        var first = arrivals[0];

        UtcInstant.TryParse(first.OccurredAt, out var occurredAtInstant);
        var arrivalTime = TimeOnly.FromDateTime(occurredAtInstant);
        var expected = TimeOnly.ParseExact(session.ScheduledStartTime, "HH:mm");
        var lateCutoff = expected.Add(TimeSpan.FromMinutes(policy.LateGraceMinutes));
        var absentCutoff = expected.Add(TimeSpan.FromMinutes(policy.AbsentAfterMinutes));

        var status = arrivalTime <= lateCutoff
            ? SessionAttendanceStatusValue.Present
            : arrivalTime <= absentCutoff
                ? SessionAttendanceStatusValue.Late
                : SessionAttendanceStatusValue.Absent;

        return new SessionMemberClassification
        {
            TenantId = tenantId,
            SessionId = session.SessionId,
            PersonId = personId,
            Status = status,
            GroundingEvidence = [first],
            PolicyVersion = policy.Version,
            ComputedAt = now,
        };
    }
}
