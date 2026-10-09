using Biolog.Features.DuplicateReview.Api;
using Biolog.Features.DuplicateReview.Audit;
using Biolog.Features.DuplicateReview.Domain;
using Biolog.Features.DuplicateReview.Identifiers;
using Xunit;

namespace Biolog.Features.DuplicateReview.Tests;

/// <summary>ADR-0003 Decision 7's audit-atomicity pattern, applied to BIO-IDN-005's own aggregate - established across BIO-TEN-001/BIO-TEN-002/BIO-DEV-001/BIO-IDN-001..004.</summary>
public sealed class DuplicateCandidateAuditTests
{
    private static readonly AuditActor Actor = new(ActorType.User, "registrar_ada");
    private const string TenantId = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77";
    private const string PersonA = "per_0199e0c1-9d40-7e52-b103-7c9a4e2f6b1d";
    private const string PersonB = "per_0199e0c2-2c35-7196-ad47-5f0a7b9c3e14";

    private static readonly IReadOnlyList<PersonAttributes> MatchingPair =
    [
        new(PersonA, "Ngozi", "Okafor", "2005-03-14"),
        new(PersonB, "Ngozi", "Okafor", "2005-03-14"),
    ];

    private static DuplicateReviewService NewService(IDuplicateCandidateRepository repository, IDuplicateCandidateAuditSink sink) =>
        new(
            new FakeCurrentTenantProvider(TenantId), repository, sink, new DuplicateCandidateLockRegistry(),
            new FakePersonAttributeGateway(MatchingPair), new FakeIdentifierAttributeGateway(new Dictionary<string, IReadOnlyList<IdentifierAttribute>>()),
            new InMemoryPersonContactPointRepository());

    [Fact]
    public async Task Raising_a_candidate_records_exactly_one_allowed_audit_event_naming_the_matched_signal_but_no_PII_value()
    {
        var ct = TestContext.Current.CancellationToken;
        var sink = new InMemoryDuplicateCandidateAuditSink();
        var service = NewService(new InMemoryDuplicateCandidateRepository(), sink);

        await service.ScanForPersonAsync(PersonA, Actor, CanonicalId.NewEventId(), ct);

        var evt = Assert.Single(sink.Events);
        Assert.Equal("duplicate_candidate.raise", evt.Action);
        Assert.Equal(AuditOutcome.Allowed, evt.Outcome);
        Assert.Equal("duplicate_candidate", evt.Target.Type);
        Assert.Equal(TenantId, evt.TenantId);
        // Compared PII (the name "Ngozi Okafor" and date of birth "2005-03-14") must never
        // appear in the audit trail - only the WIRE LABEL of which signal matched.
        Assert.DoesNotContain(evt.Detail.Values, v => v is string s && (s.Contains("Ngozi") || s.Contains("2005-03-14")));
        Assert.Equal("internal", evt.Classification);
    }

    [Fact]
    public async Task Deciding_a_candidate_records_an_audit_event_carrying_the_reason_code()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryDuplicateCandidateRepository();
        var sink = new InMemoryDuplicateCandidateAuditSink();
        var service = NewService(repository, sink);
        var raised = await service.ScanForPersonAsync(PersonA, Actor, CanonicalId.NewEventId(), ct);

        await service.DecideAsync(
            PersonA, PersonB, new DecideDuplicateCandidateRequest { Decision = "CONFIRMED_DUPLICATE", ReasonCode = "same_enrolment", Version = raised[0].Version },
            Actor, CanonicalId.NewEventId(), ct);

        var evt = sink.Events.Single(e => e.Action == "duplicate_candidate.decide");
        Assert.Equal(AuditOutcome.Allowed, evt.Outcome);
        Assert.Equal("same_enrolment", evt.ReasonCode);
    }

    [Fact]
    public async Task A_failed_validation_records_no_audit_event()
    {
        var ct = TestContext.Current.CancellationToken;
        var sink = new InMemoryDuplicateCandidateAuditSink();
        var service = NewService(new InMemoryDuplicateCandidateRepository(), sink);

        await Assert.ThrowsAsync<DuplicateCandidateError>(() => service.ScanForPersonAsync("not-a-person-id", Actor, CanonicalId.NewEventId(), ct));

        Assert.Empty(sink.Events);
    }

    [Fact]
    public async Task Raising_a_candidate_is_never_committed_when_the_audit_sink_fails()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryDuplicateCandidateRepository();
        var service = NewService(repository, new ThrowingAuditSink());

        var error = await Assert.ThrowsAsync<DuplicateCandidateError>(() => service.ScanForPersonAsync(PersonA, Actor, CanonicalId.NewEventId(), ct));

        Assert.Equal("audit_unavailable", error.Code);
        Assert.Empty(await repository.ListForPersonAsync(TenantId, PersonA, ct));
    }

    [Fact]
    public async Task Deciding_is_never_committed_when_the_audit_sink_fails()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryDuplicateCandidateRepository();
        var raised = await NewService(repository, new InMemoryDuplicateCandidateAuditSink()).ScanForPersonAsync(PersonA, Actor, CanonicalId.NewEventId(), ct);
        var service = NewService(repository, new ThrowingAuditSink());

        var error = await Assert.ThrowsAsync<DuplicateCandidateError>(() => service.DecideAsync(
            PersonA, PersonB, new DecideDuplicateCandidateRequest { Decision = "CONFIRMED_DISTINCT", ReasonCode = "x", Version = raised[0].Version },
            Actor, CanonicalId.NewEventId(), ct));

        Assert.Equal("audit_unavailable", error.Code);
        var stillStored = await repository.GetAsync(TenantId, raised[0].PersonIdLow, raised[0].PersonIdHigh, ct);
        Assert.Equal(DuplicateReviewStatus.PendingReview, stillStored!.Status); // decision never committed
    }

    [Fact]
    public async Task An_audit_write_that_durably_lands_but_throws_on_acknowledgement_still_commits_correctly()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryDuplicateCandidateRepository();
        var sink = new InMemoryDuplicateCandidateAuditSink();
        var raised = await NewService(repository, sink).ScanForPersonAsync(PersonA, Actor, CanonicalId.NewEventId(), ct);

        var service = NewService(repository, new DurablyWritesThenThrowsAuditSink(sink));
        var decided = await service.DecideAsync(
            PersonA, PersonB, new DecideDuplicateCandidateRequest { Decision = "CONFIRMED_DISTINCT", ReasonCode = "x", Version = raised[0].Version },
            Actor, CanonicalId.NewEventId(), ct);

        Assert.Equal(DuplicateReviewStatus.ConfirmedDistinct, decided.Status);
    }

    [Fact]
    public async Task A_total_audit_store_outage_where_even_verification_fails_still_fails_closed()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService(new InMemoryDuplicateCandidateRepository(), new ThrowingAuditSink());

        var error = await Assert.ThrowsAsync<DuplicateCandidateError>(() => service.ScanForPersonAsync(PersonA, Actor, CanonicalId.NewEventId(), ct));

        Assert.Equal("audit_unavailable", error.Code);
    }

    [Fact]
    public async Task Two_genuinely_concurrent_scans_of_the_same_pair_never_produce_two_candidate_records()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryDuplicateCandidateRepository();
        var plainSink = new InMemoryDuplicateCandidateAuditSink();
        var lockRegistry = new DuplicateCandidateLockRegistry(); // shared: both callers must serialize on this pair
        var delayingSink = new DelayFirstCallAuditSink(plainSink);
        var gateway = new FakePersonAttributeGateway(MatchingPair);
        var identifierGateway = new FakeIdentifierAttributeGateway(new Dictionary<string, IReadOnlyList<IdentifierAttribute>>());

        var contactPointRepository = new InMemoryPersonContactPointRepository();
        var serviceA = new DuplicateReviewService(new FakeCurrentTenantProvider(TenantId), repository, delayingSink, lockRegistry, gateway, identifierGateway, contactPointRepository);
        var serviceB = new DuplicateReviewService(new FakeCurrentTenantProvider(TenantId), repository, plainSink, lockRegistry, gateway, identifierGateway, contactPointRepository);

        var taskA = serviceA.ScanForPersonAsync(PersonA, Actor, CanonicalId.NewEventId(), ct);
        var taskB = serviceB.ScanForPersonAsync(PersonB, Actor, CanonicalId.NewEventId(), ct);
        await Task.WhenAll(taskA, taskB);

        // Exactly one candidate record exists for the pair regardless of which caller's raise
        // won the race - the loser's write is an augment (a no-op here, since both observed
        // the identical signal), never a second CreateAsync.
        var stored = await repository.ListForPersonAsync(TenantId, PersonA, ct);
        Assert.Single(stored);
        var raiseEvents = plainSink.Events.Where(e => e.Action == "duplicate_candidate.raise").ToList();
        Assert.Single(raiseEvents);
    }

    private sealed class ThrowingAuditSink : IDuplicateCandidateAuditSink
    {
        public Task WriteAsync(DuplicateCandidateAuditEvent auditEvent, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("synthetic sink outage");

        public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("synthetic sink outage: cannot even verify");
    }

    private sealed class DelayFirstCallAuditSink(IDuplicateCandidateAuditSink inner) : IDuplicateCandidateAuditSink
    {
        private int _calls;

        public async Task WriteAsync(DuplicateCandidateAuditEvent auditEvent, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
            }

            await inner.WriteAsync(auditEvent, cancellationToken);
        }

        public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) => inner.ExistsAsync(eventId, cancellationToken);
    }

    private sealed class DurablyWritesThenThrowsAuditSink(IDuplicateCandidateAuditSink inner) : IDuplicateCandidateAuditSink
    {
        public async Task WriteAsync(DuplicateCandidateAuditEvent auditEvent, CancellationToken cancellationToken = default)
        {
            await inner.WriteAsync(auditEvent, cancellationToken);
            throw new InvalidOperationException("synthetic ack failure after a successful durable write");
        }

        public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) => inner.ExistsAsync(eventId, cancellationToken);
    }
}
