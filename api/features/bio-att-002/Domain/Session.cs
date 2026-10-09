using Biolog.Features.AttendanceSessions.Identifiers;

namespace Biolog.Features.AttendanceSessions.Domain;

/// <summary>
/// AC01 - a session (lecture/class/exam/hostel/event) and its roster, a mutable, CAS-versioned
/// tenant-scoped aggregate (ADR-0001 §3.1/§3.2 - the same optimistic-concurrency shape
/// BIO-ATT-001's own <see cref="AttendancePolicy"/> and BIO-TRN-001's own Route/Vehicle/Stop use),
/// because a roster is a live enrolment list an administrator may revise between the session being
/// scheduled and it taking place - not an immutable fact.
///
/// <see cref="ScheduledDate"/>/<see cref="ScheduledStartTime"/> deliberately reuse BIO-ATT-001's
/// own wall-time convention (see <see cref="SessionAttendancePolicy"/>'s header) so
/// <see cref="SessionAttendanceClassifier"/> can query BIO-ATT-001's own
/// <c>IAttendanceEventQueryGateway.ListForPersonOnDateAsync</c> with the SAME per-(person,
/// calendar date) granularity that story's own evaluator already uses, rather than inventing a
/// second, incompatible time-window concept.
/// </summary>
public sealed class Session
{
    public string TenantId { get; }
    public string SessionId { get; }
    public SessionType SessionType { get; }
    public string Title { get; }

    /// <summary>Calendar date (yyyy-MM-dd) this session takes place - the same convention <see cref="Domain.AttendanceEvidenceRecord.OccurredAt"/>'s own date component uses.</summary>
    public string ScheduledDate { get; }

    /// <summary>"HH:mm" 24-hour, tenant-local wall time with no timezone attached - the same disclosed simplification BIO-ATT-001's own <c>AttendancePolicy.ExpectedStartTime</c> already carries.</summary>
    public string ScheduledStartTime { get; }

    public IReadOnlyList<string> RosterPersonIds { get; }

    public int Version { get; }
    public string CreatedAt { get; }
    public string CreatedBy { get; }
    public string UpdatedAt { get; }
    public string UpdatedBy { get; }

    private Session(
        string tenantId, string sessionId, SessionType sessionType, string title, string scheduledDate, string scheduledStartTime,
        IReadOnlyList<string> rosterPersonIds, int version, string createdAt, string createdBy, string updatedAt, string updatedBy)
    {
        TenantId = tenantId;
        SessionId = sessionId;
        SessionType = sessionType;
        Title = title;
        ScheduledDate = scheduledDate;
        ScheduledStartTime = scheduledStartTime;
        RosterPersonIds = rosterPersonIds;
        Version = version;
        CreatedAt = createdAt;
        CreatedBy = createdBy;
        UpdatedAt = updatedAt;
        UpdatedBy = updatedBy;
    }

    public static Session Create(
        string tenantId, SessionType sessionType, string title, string scheduledDate, string scheduledStartTime,
        IReadOnlyList<string> rosterPersonIds, string actorId, string now)
    {
        ValidateFields(title, scheduledDate, scheduledStartTime, rosterPersonIds);
        return new Session(
            tenantId, CanonicalId.NewLocalId(), sessionType, title, scheduledDate, scheduledStartTime,
            [.. rosterPersonIds.Distinct(StringComparer.Ordinal)], version: 1, createdAt: now, createdBy: actorId, updatedAt: now, updatedBy: actorId);
    }

    /// <summary>AC01 - a roster is revisable. Returns a NEW instance (never mutates this one), one version ahead - the caller (<see cref="SessionService"/>) is responsible for the compare-and-set write against <see cref="Version"/>.</summary>
    public Session UpdateRoster(IReadOnlyList<string> rosterPersonIds, string actorId, string now)
    {
        if (rosterPersonIds.Count == 0)
        {
            throw SessionError.Validation([new Api.ValidationErrorItem("roster_person_ids", "required", "roster_person_ids must contain at least one person")]);
        }

        return new Session(
            TenantId, SessionId, SessionType, Title, ScheduledDate, ScheduledStartTime,
            [.. rosterPersonIds.Distinct(StringComparer.Ordinal)], Version + 1, CreatedAt, CreatedBy, now, actorId);
    }

    private static void ValidateFields(string title, string scheduledDate, string scheduledStartTime, IReadOnlyList<string> rosterPersonIds)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Length > 200)
        {
            throw SessionError.Validation([new Api.ValidationErrorItem("title", "invalid", "title must be non-empty and at most 200 characters")]);
        }

        if (!DateOnly.TryParseExact(scheduledDate, "yyyy-MM-dd", out _))
        {
            throw SessionError.Validation([new Api.ValidationErrorItem("scheduled_date", "invalid_format", "scheduled_date must be 'yyyy-MM-dd'")]);
        }

        if (!TimeOnly.TryParseExact(scheduledStartTime, "HH:mm", out _))
        {
            throw SessionError.Validation([new Api.ValidationErrorItem("scheduled_start_time", "invalid_format", "scheduled_start_time must be 'HH:mm' 24-hour")]);
        }

        if (rosterPersonIds.Count == 0)
        {
            throw SessionError.Validation([new Api.ValidationErrorItem("roster_person_ids", "required", "roster_person_ids must contain at least one person")]);
        }
    }
}
