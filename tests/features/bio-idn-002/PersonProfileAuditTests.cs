using Biolog.Features.PersonProfileRegistry.Api;
using Biolog.Features.PersonProfileRegistry.Audit;
using Biolog.Features.PersonProfileRegistry.Domain;
using Biolog.Features.PersonProfileRegistry.Identifiers;
using Xunit;

namespace Biolog.Features.PersonProfileRegistry.Tests;

/// <summary>ADR-0003 Decision 7's audit-atomicity pattern, applied to BIO-IDN-002's own aggregate - established across BIO-TEN-001/BIO-TEN-002/BIO-DEV-001/BIO-IDN-001.</summary>
public sealed class PersonProfileAuditTests
{
    private static readonly AuditActor Actor = new(ActorType.User, "registrar_ada");
    private const string TenantId = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77";
    private const string PersonId = "per_0199e0c1-9d40-7e52-b103-7c9a4e2f6b1d";

    private static AssignProfileRequest ValidRequest(string type = "STUDENT") => new() { Type = type };

    private static PersonProfileService NewService(IPersonProfileRepository repository, IPersonProfileAuditSink sink) =>
        new(new FakeCurrentTenantProvider(TenantId), repository, sink, new PersonProfileLockRegistry(), new FakePersonExistenceGateway());

    [Fact]
    public async Task Assign_records_exactly_one_allowed_audit_event_carrying_the_real_tenant_id()
    {
        var ct = TestContext.Current.CancellationToken;
        var sink = new InMemoryPersonProfileAuditSink();
        var service = NewService(new InMemoryPersonProfileRepository(), sink);

        await service.AssignAsync(PersonId, ValidRequest(), Actor, CanonicalId.NewEventId(), ct);

        var evt = Assert.Single(sink.Events);
        Assert.Equal("person_profile.assign", evt.Action);
        Assert.Equal(AuditOutcome.Allowed, evt.Outcome);
        Assert.Equal("person_profile", evt.Target.Type);
        Assert.Equal(TenantId, evt.TenantId);
    }

    [Fact]
    public async Task Revoke_records_an_audit_event()
    {
        var ct = TestContext.Current.CancellationToken;
        var sink = new InMemoryPersonProfileAuditSink();
        var service = NewService(new InMemoryPersonProfileRepository(), sink);
        var profile = await service.AssignAsync(PersonId, ValidRequest(), Actor, CanonicalId.NewEventId(), ct);

        await service.RevokeAsync(PersonId, ProfileType.Student, new RevokeProfileRequest { Version = profile.Version }, Actor, CanonicalId.NewEventId(), ct);

        var evt = sink.Events.Single(e => e.Action == "person_profile.revoke");
        Assert.Equal(AuditOutcome.Allowed, evt.Outcome);
    }

    [Fact]
    public async Task A_failed_validation_records_no_audit_event()
    {
        var ct = TestContext.Current.CancellationToken;
        var sink = new InMemoryPersonProfileAuditSink();
        var service = NewService(new InMemoryPersonProfileRepository(), sink);

        await Assert.ThrowsAsync<PersonProfileError>(
            () => service.AssignAsync(PersonId, new AssignProfileRequest { Type = "NOT_A_TYPE" }, Actor, CanonicalId.NewEventId(), ct));

        Assert.Empty(sink.Events);
    }

    [Fact]
    public async Task A_duplicate_profile_type_attempt_records_no_audit_event()
    {
        var ct = TestContext.Current.CancellationToken;
        var sink = new InMemoryPersonProfileAuditSink();
        var service = NewService(new InMemoryPersonProfileRepository(), sink);
        await service.AssignAsync(PersonId, ValidRequest(), Actor, CanonicalId.NewEventId(), ct);
        var eventsAfterFirst = sink.Events.Count;

        await Assert.ThrowsAsync<PersonProfileError>(() => service.AssignAsync(PersonId, ValidRequest(), Actor, CanonicalId.NewEventId(), ct));

        Assert.Equal(eventsAfterFirst, sink.Events.Count);
    }

    [Fact]
    public async Task Assign_is_never_committed_when_the_audit_sink_fails()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryPersonProfileRepository();
        var service = NewService(repository, new ThrowingAuditSink());

        var error = await Assert.ThrowsAsync<PersonProfileError>(() => service.AssignAsync(PersonId, ValidRequest(), Actor, CanonicalId.NewEventId(), ct));

        Assert.Equal("audit_unavailable", error.Code);
        Assert.Empty(await repository.ListByPersonAsync(TenantId, PersonId, ct));
    }

    [Fact]
    public async Task Revoke_is_never_committed_when_the_audit_sink_fails()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryPersonProfileRepository();
        var profile = await NewService(repository, new InMemoryPersonProfileAuditSink()).AssignAsync(PersonId, ValidRequest(), Actor, CanonicalId.NewEventId(), ct);
        var service = NewService(repository, new ThrowingAuditSink());

        var error = await Assert.ThrowsAsync<PersonProfileError>(() => service.RevokeAsync(
            PersonId, ProfileType.Student, new RevokeProfileRequest { Version = profile.Version }, Actor, CanonicalId.NewEventId(), ct));

        Assert.Equal("audit_unavailable", error.Code);
        var stillStored = await repository.GetAsync(TenantId, PersonId, ProfileType.Student, ct);
        Assert.Equal(ProfileStatus.Active, stillStored!.Status);
    }

    [Fact]
    public async Task Two_genuinely_concurrent_revokes_of_the_same_profile_never_produce_a_false_allowed_audit()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryPersonProfileRepository();
        var plainSink = new InMemoryPersonProfileAuditSink();
        var lockRegistry = new PersonProfileLockRegistry(); // shared: both callers must serialize
        var profile = await new PersonProfileService(new FakeCurrentTenantProvider(TenantId), repository, plainSink, lockRegistry, new FakePersonExistenceGateway())
            .AssignAsync(PersonId, ValidRequest(), Actor, CanonicalId.NewEventId(), ct);

        var delayingSink = new DelayFirstCallAuditSink(plainSink);
        var service = new PersonProfileService(new FakeCurrentTenantProvider(TenantId), repository, delayingSink, lockRegistry, new FakePersonExistenceGateway());

        var taskA = TryRunAsync(() => service.RevokeAsync(PersonId, ProfileType.Student, new RevokeProfileRequest { Version = profile.Version }, Actor, CanonicalId.NewEventId(), ct));
        var taskB = TryRunAsync(() => service.RevokeAsync(PersonId, ProfileType.Student, new RevokeProfileRequest { Version = profile.Version }, Actor, CanonicalId.NewEventId(), ct));

        var (resultA, errorA) = await taskA;
        var (resultB, errorB) = await taskB;

        // The lock fully serializes the two callers. Both supplied the SAME (now-stale-for-
        // whichever-runs-second) expected version, so the second to run fails the version
        // check before it ever reaches the no-op "already revoked" branch - a stale version
        // is always a conflict, regardless of what the target status ends up being. Exactly
        // one caller succeeds, the other gets concurrency_conflict, and only ONE revoke event
        // was ever recorded.
        var winner = resultA ?? resultB;
        var loserError = errorA ?? errorB;
        Assert.NotNull(winner);
        Assert.NotNull(loserError);
        Assert.Equal("concurrency_conflict", loserError!.Code);
        var revokeEvents = plainSink.Events.Where(e => e.Action == "person_profile.revoke").ToList();
        Assert.Single(revokeEvents);
    }

    [Fact]
    public async Task An_audit_write_that_durably_lands_but_throws_on_acknowledgement_still_commits_correctly()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryPersonProfileRepository();
        var sink = new InMemoryPersonProfileAuditSink();
        var profile = await NewService(repository, sink).AssignAsync(PersonId, ValidRequest(), Actor, CanonicalId.NewEventId(), ct);

        var service = NewService(repository, new DurablyWritesThenThrowsAuditSink(sink));
        var revoked = await service.RevokeAsync(PersonId, ProfileType.Student, new RevokeProfileRequest { Version = profile.Version }, Actor, CanonicalId.NewEventId(), ct);

        Assert.Equal(ProfileStatus.Revoked, revoked.Status);
    }

    [Fact]
    public async Task A_total_audit_store_outage_where_even_verification_fails_still_fails_closed()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService(new InMemoryPersonProfileRepository(), new ThrowingAuditSink());

        var error = await Assert.ThrowsAsync<PersonProfileError>(() => service.AssignAsync(PersonId, ValidRequest(), Actor, CanonicalId.NewEventId(), ct));

        Assert.Equal("audit_unavailable", error.Code);
    }

    [Fact]
    public async Task A_cancellation_arriving_right_after_the_audit_write_succeeds_does_not_abort_the_commit()
    {
        var ct = TestContext.Current.CancellationToken;
        using var cts = new CancellationTokenSource();
        var repository = new ThrowsOnCancelledTokenRepository(new InMemoryPersonProfileRepository());
        var sink = new CancelsCallerTokenAfterWriteAuditSink(new InMemoryPersonProfileAuditSink(), cts);
        var service = NewService(repository, sink);

        var profile = await service.AssignAsync(PersonId, ValidRequest(), Actor, CanonicalId.NewEventId(), cts.Token);

        Assert.True(cts.IsCancellationRequested);
        Assert.NotNull(await repository.GetAsync(TenantId, PersonId, ProfileType.Student, ct));
    }

    private static async Task<(PersonProfile? Result, PersonProfileError? Error)> TryRunAsync(Func<Task<PersonProfile>> action)
    {
        try
        {
            return (await action(), null);
        }
        catch (PersonProfileError ex)
        {
            return (null, ex);
        }
    }

    private sealed class ThrowingAuditSink : IPersonProfileAuditSink
    {
        public Task WriteAsync(PersonProfileAuditEvent auditEvent, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("synthetic sink outage");

        public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("synthetic sink outage: cannot even verify");
    }

    private sealed class DelayFirstCallAuditSink(IPersonProfileAuditSink inner) : IPersonProfileAuditSink
    {
        private int _calls;

        public async Task WriteAsync(PersonProfileAuditEvent auditEvent, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
            }

            await inner.WriteAsync(auditEvent, cancellationToken);
        }

        public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) =>
            inner.ExistsAsync(eventId, cancellationToken);
    }

    private sealed class DurablyWritesThenThrowsAuditSink(IPersonProfileAuditSink inner) : IPersonProfileAuditSink
    {
        public async Task WriteAsync(PersonProfileAuditEvent auditEvent, CancellationToken cancellationToken = default)
        {
            await inner.WriteAsync(auditEvent, cancellationToken);
            throw new InvalidOperationException("synthetic ack failure after a successful durable write");
        }

        public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) =>
            inner.ExistsAsync(eventId, cancellationToken);
    }

    private sealed class CancelsCallerTokenAfterWriteAuditSink(IPersonProfileAuditSink inner, CancellationTokenSource cts) : IPersonProfileAuditSink
    {
        public async Task WriteAsync(PersonProfileAuditEvent auditEvent, CancellationToken cancellationToken = default)
        {
            await inner.WriteAsync(auditEvent, cancellationToken);
            cts.Cancel();
        }

        public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) =>
            inner.ExistsAsync(eventId, cancellationToken);
    }

    private sealed class ThrowsOnCancelledTokenRepository(IPersonProfileRepository inner) : IPersonProfileRepository
    {
        public Task<PersonProfile> CreateAsync(PersonProfile profile, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return inner.CreateAsync(profile, cancellationToken);
        }

        public Task<PersonProfile?> GetAsync(string tenantId, string personId, ProfileType type, CancellationToken cancellationToken = default) =>
            inner.GetAsync(tenantId, personId, type, cancellationToken);

        public Task<IReadOnlyList<PersonProfile>> ListByPersonAsync(string tenantId, string personId, CancellationToken cancellationToken = default) =>
            inner.ListByPersonAsync(tenantId, personId, cancellationToken);

        public Task<PersonProfile> ReplaceAsync(PersonProfile expected, PersonProfile updated, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return inner.ReplaceAsync(expected, updated, cancellationToken);
        }
    }
}
