using Biolog.Features.AttendanceSessions.Domain;
using Biolog.Features.AttendanceSessions.Identifiers;
using Biolog.Features.AttendanceSessions.Tenancy;

namespace Biolog.Features.AttendanceSessions;

/// <summary>
/// AC01/AC02/AC03's read-time composition: fetches a session, its roster's raw evidence (via
/// <see cref="IAttendanceEventQueryGateway"/>, BIO-ATT-001's own events), the tenant's current
/// <see cref="SessionAttendancePolicy"/> for that session type, and any recorded
/// <see cref="AttendanceAdjustment"/>s - then hands each roster member's evidence and the policy to
/// the pure <see cref="SessionAttendanceClassifier"/>, and layers the latest adjustment (if any) on
/// top (never replacing the computed classification - AC04's own never-mutate discipline). Nothing
/// here writes anything - a roster view is computed fresh on every call, never cached or stored,
/// the same "compute in effect at read time" discipline BIO-ATT-001's own
/// <c>AttendanceStatusService</c> established.
///
/// <b>Stays open to any asserted actor (ADR-0006 Decision 2).</b> Reading a roster's classification
/// is an audit-attribution use only, the same category as BIO-ATT-001's own <c>GetStatusAsync</c>.
/// </summary>
internal sealed class SessionAttendanceQueryService(
    ICurrentTenantProvider tenantProvider,
    ISessionRepository sessionRepository,
    ISessionAttendancePolicyRepository policyRepository,
    IAttendanceAdjustmentRepository adjustmentRepository,
    IAttendanceEventQueryGateway eventGateway)
{
    public async Task<(Session Session, IReadOnlyList<SessionRosterMemberView> Roster)> GetRosterAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();

        var session = await sessionRepository.GetAsync(tenantId, sessionId, cancellationToken) ?? throw SessionError.SessionNotFound(sessionId);
        var policy = await policyRepository.GetAsync(tenantId, session.SessionType, cancellationToken);
        var sessionAdjustments = await adjustmentRepository.ListForSessionAsync(tenantId, sessionId, cancellationToken);

        var now = UtcInstant.Now();
        var roster = new List<SessionRosterMemberView>(session.RosterPersonIds.Count);

        foreach (var personId in session.RosterPersonIds)
        {
            var evidence = await eventGateway.ListForPersonOnDateAsync(personId, session.ScheduledDate, cancellationToken);
            var classification = SessionAttendanceClassifier.Evaluate(tenantId, session, personId, evidence, policy, now);
            var latestAdjustment = sessionAdjustments
                .Where(a => a.PersonId == personId)
                .OrderByDescending(a => a.AdjustedAt, StringComparer.Ordinal)
                .FirstOrDefault();

            roster.Add(new SessionRosterMemberView { PersonId = personId, Classification = classification, LatestAdjustment = latestAdjustment });
        }

        return (session, roster);
    }

    /// <summary>Single-member classification - the same composition <see cref="GetRosterAsync"/> performs, scoped to one person, used by <see cref="AttendanceAdjustmentService"/> to capture the pre-adjustment status (AC04).</summary>
    public async Task<SessionRosterMemberView> GetMemberAsync(Session session, string personId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        var policy = await policyRepository.GetAsync(tenantId, session.SessionType, cancellationToken);
        var evidence = await eventGateway.ListForPersonOnDateAsync(personId, session.ScheduledDate, cancellationToken);
        var classification = SessionAttendanceClassifier.Evaluate(tenantId, session, personId, evidence, policy, UtcInstant.Now());
        var latestAdjustment = (await adjustmentRepository.ListForPersonAsync(tenantId, session.SessionId, personId, cancellationToken))
            .OrderByDescending(a => a.AdjustedAt, StringComparer.Ordinal)
            .FirstOrDefault();

        return new SessionRosterMemberView { PersonId = personId, Classification = classification, LatestAdjustment = latestAdjustment };
    }

    private string RequireTenant() => tenantProvider.TenantId ?? throw SessionError.TenantContextMissing();
}
