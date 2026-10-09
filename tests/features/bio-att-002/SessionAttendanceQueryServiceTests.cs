using Biolog.Features.AttendanceSessions.Domain;
using Xunit;

namespace Biolog.Features.AttendanceSessions.Tests;

public sealed class SessionAttendanceQueryServiceTests
{
    private const string TenantId = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77";
    private const string PersonA = "per_0199e0c1-9d40-7e52-b103-7c9a4e2f6b1d";
    private const string PersonB = "per_0199e0c2-1111-7222-8333-444455556666";

    private static (SessionAttendanceQueryService Service, InMemorySessionRepository Sessions, InMemorySessionAttendancePolicyRepository Policies, InMemoryAttendanceAdjustmentRepository Adjustments, FakeAttendanceEventQueryGateway Evidence) Build()
    {
        var sessions = new InMemorySessionRepository();
        var policies = new InMemorySessionAttendancePolicyRepository();
        var adjustments = new InMemoryAttendanceAdjustmentRepository();
        var evidence = new FakeAttendanceEventQueryGateway();
        var service = new SessionAttendanceQueryService(new FakeCurrentTenantProvider(TenantId), sessions, policies, adjustments, evidence);
        return (service, sessions, policies, adjustments, evidence);
    }

    [Fact]
    public async Task AC01_AC03_a_roster_read_returns_every_member_with_their_own_grounding_evidence()
    {
        var (service, sessions, _, _, evidence) = Build();
        var session = Session.Create(TenantId, SessionType.Lecture, "Intro to Biology", "2026-10-01", "09:00", [PersonA, PersonB], "usr_teacher", "2026-09-30T00:00:00.000Z");
        await sessions.CreateAsync(session, CancellationToken.None);

        evidence.EvidenceByPersonAndDate[(PersonA, "2026-10-01")] = [new AttendanceEvidenceRecord
        {
            EventId = "evt_0199e0c1-3333-7444-8555-000000000000",
            OccurredAt = "2026-10-01T09:05:00.000Z",
            Source = "device",
            Mode = "check_in",
            EvidenceMethod = "credential",
            EvidenceVerified = true,
            EvidenceConfidence = 0.9,
        }];
        // PersonB has no evidence at all.

        var (_, roster) = await service.GetRosterAsync(session.SessionId, CancellationToken.None);

        Assert.Equal(2, roster.Count);
        var personAView = roster.Single(r => r.PersonId == PersonA);
        var personBView = roster.Single(r => r.PersonId == PersonB);

        Assert.Equal(SessionAttendanceStatusValue.Present, personAView.Classification.Status);
        Assert.Single(personAView.Classification.GroundingEvidence);
        Assert.Equal(SessionAttendanceStatusValue.Absent, personBView.Classification.Status);
        Assert.Empty(personBView.Classification.GroundingEvidence);
        Assert.Null(personAView.LatestAdjustment);
    }

    [Fact]
    public async Task AC04_an_adjustment_changes_the_effective_status_but_never_the_computed_classification()
    {
        var (service, sessions, _, adjustments, evidence) = Build();
        var session = Session.Create(TenantId, SessionType.Lecture, "Intro to Biology", "2026-10-01", "09:00", [PersonA], "usr_teacher", "2026-09-30T00:00:00.000Z");
        await sessions.CreateAsync(session, CancellationToken.None);
        // No evidence at all for PersonA -> computed status is Absent.

        var adjustment = AttendanceAdjustment.Create(
            TenantId, session.SessionId, PersonA, SessionAttendanceStatusValue.Absent, SessionAttendanceStatusValue.Present,
            "documented illness, excused", "usr_admin", "2026-10-01T12:00:00.000Z");
        await adjustments.CreateAsync(adjustment, CancellationToken.None);

        var (_, roster) = await service.GetRosterAsync(session.SessionId, CancellationToken.None);
        var view = Assert.Single(roster);

        // The computed classification is STILL Absent (nothing was mutated) - only the effective
        // status, layered on top, reflects the adjustment.
        Assert.Equal(SessionAttendanceStatusValue.Absent, view.Classification.Status);
        Assert.Equal(SessionAttendanceStatusValue.Present, view.EffectiveStatus);
        Assert.NotNull(view.LatestAdjustment);
        Assert.Equal("documented illness, excused", view.LatestAdjustment!.Reason);
    }

    [Fact]
    public async Task AC04_the_most_recently_adjusted_record_wins_when_more_than_one_exists_for_the_same_person()
    {
        var (service, sessions, _, adjustments, _) = Build();
        var session = Session.Create(TenantId, SessionType.Lecture, "Intro to Biology", "2026-10-01", "09:00", [PersonA], "usr_teacher", "2026-09-30T00:00:00.000Z");
        await sessions.CreateAsync(session, CancellationToken.None);

        await adjustments.CreateAsync(AttendanceAdjustment.Create(
            TenantId, session.SessionId, PersonA, SessionAttendanceStatusValue.Absent, SessionAttendanceStatusValue.Late,
            "first pass", "usr_admin", "2026-10-01T11:00:00.000Z"), CancellationToken.None);
        await adjustments.CreateAsync(AttendanceAdjustment.Create(
            TenantId, session.SessionId, PersonA, SessionAttendanceStatusValue.Absent, SessionAttendanceStatusValue.Present,
            "corrected after review", "usr_admin", "2026-10-01T13:00:00.000Z"), CancellationToken.None);

        var (_, roster) = await service.GetRosterAsync(session.SessionId, CancellationToken.None);
        var view = Assert.Single(roster);

        Assert.Equal(SessionAttendanceStatusValue.Present, view.EffectiveStatus);
        Assert.Equal("corrected after review", view.LatestAdjustment!.Reason);

        // Both adjustments still exist in the repository - the audit trail is never overwritten.
        var history = await adjustments.ListForPersonAsync(TenantId, session.SessionId, PersonA, CancellationToken.None);
        Assert.Equal(2, history.Count);
    }

    [Fact]
    public async Task AC01_reading_the_roster_of_a_session_that_does_not_exist_fails_closed()
    {
        var (service, _, _, _, _) = Build();
        await Assert.ThrowsAsync<SessionError>(() => service.GetRosterAsync("does-not-exist", CancellationToken.None));
    }
}
