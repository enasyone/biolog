using Biolog.Features.PersonRegistry.Api;
using Biolog.Features.PersonRegistry.Audit;
using Biolog.Features.PersonRegistry.Domain;
using Biolog.Features.PersonRegistry.Identifiers;
using Xunit;

namespace Biolog.Features.PersonRegistry.Tests;

/// <summary>
/// ADR-0003 Decision 7's audit-atomicity pattern, applied to BIO-IDN-001's own aggregate -
/// this story does not have to discover the pattern the hard way (BIO-TEN-001 did, across
/// three rounds); it is now a codified, cross-cutting requirement, confirmed independently by
/// BIO-TEN-002 and BIO-DEV-001. Every element still needs its own proof against THIS
/// aggregate's actual shape and constructor signatures.
/// </summary>
public sealed class PersonAuditTests
{
    private static readonly AuditActor Actor = new(ActorType.User, "registrar_ada");
    private const string TenantId = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77";

    private static RegisterPersonRequest ValidRequest(string firstName = "Ngozi", string lastName = "Okafor") =>
        new() { FirstName = firstName, LastName = lastName };

    private static PersonService NewService(IPersonRepository repository, IPersonAuditSink sink) =>
        new(new FakeCurrentTenantProvider(TenantId), repository, sink, new PersonLockRegistry());

    [Fact]
    public async Task Register_records_exactly_one_allowed_audit_event_carrying_the_real_tenant_id()
    {
        var ct = TestContext.Current.CancellationToken;
        var sink = new InMemoryPersonAuditSink();
        var service = NewService(new InMemoryPersonRepository(), sink);

        var person = await service.RegisterAsync(ValidRequest(), Actor, CanonicalId.NewEventId(), ct);

        var evt = Assert.Single(sink.Events);
        Assert.Equal("person.register", evt.Action);
        Assert.Equal(AuditOutcome.Allowed, evt.Outcome);
        Assert.Equal("person", evt.Target.Type);
        Assert.Equal(person.Id, evt.Target.Id);
        Assert.Equal(TenantId, evt.TenantId); // tenant-plane action, unlike BIO-TEN-002's null
    }

    [Fact]
    public async Task Register_never_carries_a_PII_value_in_the_audit_event_only_field_names_in_Withheld()
    {
        var ct = TestContext.Current.CancellationToken;
        var sink = new InMemoryPersonAuditSink();
        var service = NewService(new InMemoryPersonRepository(), sink);

        await service.RegisterAsync(
            new RegisterPersonRequest { FirstName = "Ngozi", LastName = "Okafor", BornOn = "2004-11-02", Title = "Dr" },
            Actor, CanonicalId.NewEventId(), ct);

        var evt = Assert.Single(sink.Events);
        Assert.DoesNotContain("Ngozi", evt.Detail.Values.Select(v => v?.ToString()));
        Assert.DoesNotContain("Okafor", evt.Detail.Values.Select(v => v?.ToString()));
        Assert.DoesNotContain("2004-11-02", evt.Detail.Values.Select(v => v?.ToString()));
        foreach (var field in new[] { "title", "first_name", "middle_name", "last_name", "preferred_name", "born_on" })
        {
            Assert.Contains(field, evt.Withheld);
        }

        Assert.Equal("internal", evt.Classification); // the only Detail value (status) is internal, never personal
    }

    [Fact]
    public async Task UpdateStatus_records_from_and_to_status()
    {
        var ct = TestContext.Current.CancellationToken;
        var sink = new InMemoryPersonAuditSink();
        var service = NewService(new InMemoryPersonRepository(), sink);
        var person = await service.RegisterAsync(ValidRequest(), Actor, CanonicalId.NewEventId(), ct);

        await service.UpdateStatusAsync(
            person.Id, new UpdatePersonStatusRequest { Version = person.Version, Status = "VERIFIED" }, Actor, CanonicalId.NewEventId(), ct);

        var evt = sink.Events.Single(e => e.Action == "person.status_change");
        Assert.Equal("PRE_ENROLLED", evt.Detail["from_status"]);
        Assert.Equal("VERIFIED", evt.Detail["to_status"]);
    }

    [Fact]
    public async Task A_failed_validation_records_no_audit_event()
    {
        var ct = TestContext.Current.CancellationToken;
        var sink = new InMemoryPersonAuditSink();
        var service = NewService(new InMemoryPersonRepository(), sink);

        await Assert.ThrowsAsync<PersonError>(
            () => service.RegisterAsync(new RegisterPersonRequest { FirstName = "", LastName = "Okafor" }, Actor, CanonicalId.NewEventId(), ct));

        Assert.Empty(sink.Events);
    }

    [Fact]
    public async Task An_illegal_transition_attempt_records_no_audit_event()
    {
        var ct = TestContext.Current.CancellationToken;
        var sink = new InMemoryPersonAuditSink();
        var service = NewService(new InMemoryPersonRepository(), sink);
        var person = await service.RegisterAsync(ValidRequest(), Actor, CanonicalId.NewEventId(), ct);
        var eventsAfterRegister = sink.Events.Count;

        await Assert.ThrowsAsync<PersonError>(() => service.UpdateStatusAsync(
            person.Id, new UpdatePersonStatusRequest { Version = person.Version, Status = "ACTIVE" }, Actor, CanonicalId.NewEventId(), ct));

        Assert.Equal(eventsAfterRegister, sink.Events.Count);
    }

    [Fact]
    public async Task Register_is_never_committed_when_the_audit_sink_fails()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryPersonRepository();
        var service = NewService(repository, new ThrowingAuditSink());

        var error = await Assert.ThrowsAsync<PersonError>(() => service.RegisterAsync(ValidRequest(), Actor, CanonicalId.NewEventId(), ct));

        Assert.Equal("audit_unavailable", error.Code);
        Assert.Empty(await repository.ListByTenantAsync(TenantId, ct)); // never reached repository.CreateAsync
    }

    [Fact]
    public async Task UpdateStatus_is_never_committed_when_the_audit_sink_fails()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryPersonRepository();
        var person = await NewService(repository, new InMemoryPersonAuditSink()).RegisterAsync(ValidRequest(), Actor, CanonicalId.NewEventId(), ct);
        var service = NewService(repository, new ThrowingAuditSink());

        var error = await Assert.ThrowsAsync<PersonError>(() => service.UpdateStatusAsync(
            person.Id, new UpdatePersonStatusRequest { Version = person.Version, Status = "VERIFIED" }, Actor, CanonicalId.NewEventId(), ct));

        Assert.Equal("audit_unavailable", error.Code);
        var stillStored = await repository.GetAsync(TenantId, person.Id, ct);
        Assert.Equal(PersonStatus.PreEnrolled, stillStored!.Status); // untouched
        Assert.Equal(person.Version, stillStored.Version);
    }

    [Fact]
    public async Task Two_genuinely_concurrent_status_updates_of_the_same_person_never_produce_a_false_allowed_audit()
    {
        // Reproduces BIO-TEN-001's round-3 proof against this story's own aggregate: two
        // "concurrent" callers of the SAME person must fully serialize under the per-person
        // lock, and the loser's rejected commit must never be described by a durably-ALLOWED
        // audit event.
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryPersonRepository();
        var plainSink = new InMemoryPersonAuditSink();
        var lockRegistry = new PersonLockRegistry(); // shared: both callers below must serialize against each other
        var person = await new PersonService(new FakeCurrentTenantProvider(TenantId), repository, plainSink, lockRegistry)
            .RegisterAsync(ValidRequest(), Actor, CanonicalId.NewEventId(), ct);
        var verified = await new PersonService(new FakeCurrentTenantProvider(TenantId), repository, plainSink, lockRegistry).UpdateStatusAsync(
            person.Id, new UpdatePersonStatusRequest { Version = person.Version, Status = "VERIFIED" }, Actor, CanonicalId.NewEventId(), ct);
        var active = await new PersonService(new FakeCurrentTenantProvider(TenantId), repository, plainSink, lockRegistry).UpdateStatusAsync(
            verified.Id, new UpdatePersonStatusRequest { Version = verified.Version, Status = "ACTIVE" }, Actor, CanonicalId.NewEventId(), ct);

        var delayingSink = new DelayFirstCallAuditSink(plainSink);
        var service = new PersonService(new FakeCurrentTenantProvider(TenantId), repository, delayingSink, lockRegistry);

        var taskA = TryRunAsync(() => service.UpdateStatusAsync(
            active.Id, new UpdatePersonStatusRequest { Version = active.Version, Status = "SUSPENDED" }, Actor, CanonicalId.NewEventId(), ct));
        var taskB = TryRunAsync(() => service.UpdateStatusAsync(
            active.Id, new UpdatePersonStatusRequest { Version = active.Version, Status = "EXPIRED" }, Actor, CanonicalId.NewEventId(), ct));

        var (resultA, errorA) = await taskA;
        var (resultB, errorB) = await taskB;

        var winner = resultA ?? resultB;
        var loserError = errorA ?? errorB;
        Assert.NotNull(winner);
        Assert.NotNull(loserError);
        Assert.Equal("concurrency_conflict", loserError!.Code);

        var statusChangeEvents = plainSink.Events.Where(e => e.Action == "person.status_change" && e.Detail["from_status"]!.Equals("ACTIVE")).ToList();
        var evt = Assert.Single(statusChangeEvents);
        Assert.Equal(AuditOutcome.Allowed, evt.Outcome);

        var stored = await repository.GetAsync(TenantId, active.Id, ct);
        Assert.Equal(winner!.Status, stored!.Status);
    }

    [Fact]
    public async Task An_audit_write_that_durably_lands_but_throws_on_acknowledgement_still_commits_correctly()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryPersonRepository();
        var sink = new InMemoryPersonAuditSink();
        var person = await NewService(repository, sink).RegisterAsync(ValidRequest(), Actor, CanonicalId.NewEventId(), ct);

        var service = NewService(repository, new DurablyWritesThenThrowsAuditSink(sink));
        var updated = await service.UpdateStatusAsync(
            person.Id, new UpdatePersonStatusRequest { Version = person.Version, Status = "VERIFIED" }, Actor, CanonicalId.NewEventId(), ct);

        Assert.Equal(PersonStatus.Verified, updated.Status); // succeeded, not audit_unavailable
        var stored = await repository.GetAsync(TenantId, person.Id, ct);
        Assert.Equal(updated, stored);
    }

    [Fact]
    public async Task A_total_audit_store_outage_where_even_verification_fails_still_fails_closed()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService(new InMemoryPersonRepository(), new ThrowingAuditSink());

        var error = await Assert.ThrowsAsync<PersonError>(() => service.RegisterAsync(ValidRequest(), Actor, CanonicalId.NewEventId(), ct));

        Assert.Equal("audit_unavailable", error.Code);
    }

    [Fact]
    public async Task A_cancellation_arriving_right_after_the_audit_write_succeeds_does_not_abort_the_commit()
    {
        var ct = TestContext.Current.CancellationToken;
        using var cts = new CancellationTokenSource();
        var repository = new ThrowsOnCancelledTokenRepository(new InMemoryPersonRepository());
        var sink = new CancelsCallerTokenAfterWriteAuditSink(new InMemoryPersonAuditSink(), cts);
        var service = NewService(repository, sink);

        var person = await service.RegisterAsync(ValidRequest(), Actor, CanonicalId.NewEventId(), cts.Token);

        Assert.True(cts.IsCancellationRequested);
        Assert.NotNull(await repository.GetAsync(TenantId, person.Id, ct));
    }

    private static async Task<(Person? Result, PersonError? Error)> TryRunAsync(Func<Task<Person>> action)
    {
        try
        {
            return (await action(), null);
        }
        catch (PersonError ex)
        {
            return (null, ex);
        }
    }

    private sealed class ThrowingAuditSink : IPersonAuditSink
    {
        public Task WriteAsync(PersonAuditEvent auditEvent, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("synthetic sink outage");

        public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("synthetic sink outage: cannot even verify");
    }

    private sealed class DelayFirstCallAuditSink(IPersonAuditSink inner) : IPersonAuditSink
    {
        private int _calls;

        public async Task WriteAsync(PersonAuditEvent auditEvent, CancellationToken cancellationToken = default)
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

    private sealed class DurablyWritesThenThrowsAuditSink(IPersonAuditSink inner) : IPersonAuditSink
    {
        public async Task WriteAsync(PersonAuditEvent auditEvent, CancellationToken cancellationToken = default)
        {
            await inner.WriteAsync(auditEvent, cancellationToken);
            throw new InvalidOperationException("synthetic ack failure after a successful durable write");
        }

        public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) =>
            inner.ExistsAsync(eventId, cancellationToken);
    }

    private sealed class CancelsCallerTokenAfterWriteAuditSink(IPersonAuditSink inner, CancellationTokenSource cts) : IPersonAuditSink
    {
        public async Task WriteAsync(PersonAuditEvent auditEvent, CancellationToken cancellationToken = default)
        {
            await inner.WriteAsync(auditEvent, cancellationToken);
            cts.Cancel();
        }

        public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) =>
            inner.ExistsAsync(eventId, cancellationToken);
    }

    private sealed class ThrowsOnCancelledTokenRepository(IPersonRepository inner) : IPersonRepository
    {
        public Task<Person> CreateAsync(Person person, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return inner.CreateAsync(person, cancellationToken);
        }

        public Task<Person?> GetAsync(string tenantId, string personId, CancellationToken cancellationToken = default) =>
            inner.GetAsync(tenantId, personId, cancellationToken);

        public Task<IReadOnlyList<Person>> ListByTenantAsync(string tenantId, CancellationToken cancellationToken = default) =>
            inner.ListByTenantAsync(tenantId, cancellationToken);

        public Task<Person> ReplaceAsync(Person expected, Person updated, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return inner.ReplaceAsync(expected, updated, cancellationToken);
        }
    }
}
