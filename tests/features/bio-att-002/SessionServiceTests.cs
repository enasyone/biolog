using Biolog.Features.AttendanceSessions.Api;
using Biolog.Features.AttendanceSessions.Audit;
using Biolog.Features.AttendanceSessions.Domain;
using Xunit;

namespace Biolog.Features.AttendanceSessions.Tests;

public sealed class SessionServiceTests
{
    private const string TenantId = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77";
    private const string PersonA = "per_0199e0c1-9d40-7e52-b103-7c9a4e2f6b1d";
    private const string PersonB = "per_0199e0c2-1111-7222-8333-444455556666";
    private static readonly AuditActor Actor = new(ActorType.User, "usr_teacher_1");

    private static SessionService BuildService(
        out InMemorySessionRepository repository, out InMemorySessionAuditSink auditSink, IReadOnlySet<string>? existingPersonIds = null)
    {
        repository = new InMemorySessionRepository();
        auditSink = new InMemorySessionAuditSink();
        return new SessionService(
            new FakeCurrentTenantProvider(TenantId), repository, auditSink, new SessionLockRegistry(),
            new FakePersonExistenceGateway(existingPersonIds));
    }

    [Fact]
    public async Task AC01_a_created_session_carries_its_session_type_and_the_full_roster_it_was_given()
    {
        var service = BuildService(out _, out _);

        var session = await service.CreateAsync(new CreateSessionRequest
        {
            SessionType = "lecture",
            Title = "Intro to Biology",
            ScheduledDate = "2026-10-01",
            ScheduledStartTime = "09:00",
            RosterPersonIds = [PersonA, PersonB],
        }, Actor, "evt_0199e0c1-0000-7000-8000-000000000001", CancellationToken.None);

        Assert.Equal(SessionType.Lecture, session.SessionType);
        Assert.Equal([PersonA, PersonB], session.RosterPersonIds);
        Assert.Equal(1, session.Version);
    }

    [Fact]
    public async Task AC01_creating_a_session_with_a_roster_person_who_does_not_exist_is_rejected()
    {
        var service = BuildService(out _, out _, existingPersonIds: new HashSet<string> { PersonA });

        await Assert.ThrowsAsync<SessionError>(() => service.CreateAsync(new CreateSessionRequest
        {
            SessionType = "class",
            Title = "Roll call",
            ScheduledDate = "2026-10-01",
            ScheduledStartTime = "09:00",
            RosterPersonIds = [PersonA, PersonB],
        }, Actor, "evt_0199e0c1-0000-7000-8000-000000000002", CancellationToken.None));
    }

    [Fact]
    public async Task AC01_a_roster_can_be_revised_after_the_session_exists_and_the_version_advances()
    {
        var service = BuildService(out _, out _);
        var session = await service.CreateAsync(new CreateSessionRequest
        {
            SessionType = "hostel",
            Title = "Evening roll call",
            ScheduledDate = "2026-10-01",
            ScheduledStartTime = "20:00",
            RosterPersonIds = [PersonA],
        }, Actor, "evt_0199e0c1-0000-7000-8000-000000000003", CancellationToken.None);

        var updated = await service.UpdateRosterAsync(session.SessionId, new UpdateSessionRosterRequest
        {
            ExpectedVersion = session.Version,
            RosterPersonIds = [PersonA, PersonB],
        }, Actor, "evt_0199e0c1-0000-7000-8000-000000000004", CancellationToken.None);

        Assert.Equal([PersonA, PersonB], updated.RosterPersonIds);
        Assert.Equal(session.Version + 1, updated.Version);
    }

    [Fact]
    public async Task AC01_a_roster_update_against_a_stale_version_is_a_concurrency_conflict()
    {
        var service = BuildService(out _, out _);
        var session = await service.CreateAsync(new CreateSessionRequest
        {
            SessionType = "exam",
            Title = "Midterm",
            ScheduledDate = "2026-10-01",
            ScheduledStartTime = "10:00",
            RosterPersonIds = [PersonA],
        }, Actor, "evt_0199e0c1-0000-7000-8000-000000000005", CancellationToken.None);

        await service.UpdateRosterAsync(session.SessionId, new UpdateSessionRosterRequest
        {
            ExpectedVersion = session.Version,
            RosterPersonIds = [PersonA, PersonB],
        }, Actor, "evt_0199e0c1-0000-7000-8000-000000000006", CancellationToken.None);

        var error = await Assert.ThrowsAsync<SessionError>(() => service.UpdateRosterAsync(session.SessionId, new UpdateSessionRosterRequest
        {
            ExpectedVersion = session.Version, // stale - already advanced above
            RosterPersonIds = [PersonB],
        }, Actor, "evt_0199e0c1-0000-7000-8000-000000000007", CancellationToken.None));

        Assert.Equal("concurrency_conflict", error.Code);
    }

    [Fact]
    public async Task AC01_a_test_proves_session_creation_and_roster_updates_are_each_audited()
    {
        var service = BuildService(out _, out var auditSink);
        var session = await service.CreateAsync(new CreateSessionRequest
        {
            SessionType = "event",
            Title = "Open day",
            ScheduledDate = "2026-10-02",
            ScheduledStartTime = "13:00",
            RosterPersonIds = [PersonA],
        }, Actor, "evt_0199e0c1-0000-7000-8000-000000000008", CancellationToken.None);

        await service.UpdateRosterAsync(session.SessionId, new UpdateSessionRosterRequest
        {
            ExpectedVersion = session.Version,
            RosterPersonIds = [PersonA, PersonB],
        }, Actor, "evt_0199e0c1-0000-7000-8000-000000000009", CancellationToken.None);

        var actions = auditSink.All().Select(e => e.Action).ToList();
        Assert.Contains("attendance_session.created", actions);
        Assert.Contains("attendance_session.roster_updated", actions);
    }
}
