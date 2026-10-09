using Biolog.Features.InstitutionalIdentifiers.Api;
using Biolog.Features.InstitutionalIdentifiers.Audit;
using Biolog.Features.InstitutionalIdentifiers.Domain;
using Biolog.Features.InstitutionalIdentifiers.Identifiers;
using Xunit;

namespace Biolog.Features.InstitutionalIdentifiers.Tests;

/// <summary>ADR-0003 Decision 7's audit-atomicity pattern, applied to BIO-IDN-003's own aggregate - established across BIO-TEN-001/BIO-TEN-002/BIO-DEV-001/BIO-IDN-001/BIO-IDN-002.</summary>
public sealed class InstitutionalIdentifierAuditTests
{
    private static readonly AuditActor Actor = new(ActorType.User, "registrar_ada");
    private const string TenantId = "ten_0199e0c1-8b2f-7c31-9a4e-2f6b1d0c5a77";
    private const string PersonId = "per_0199e0c1-9d40-7e52-b103-7c9a4e2f6b1d";
    private const string PersonId2 = "per_0199e0c2-2c35-7196-ad47-5f0a7b9c3e14";

    private static AssignIdentifierRequest ValidRequest(string value = "MAT/2026/00123") => new() { Type = "MATRICULATION_NUMBER", Value = value };

    private static InstitutionalIdentifierService NewService(IInstitutionalIdentifierRepository repository, IInstitutionalIdentifierAuditSink sink) =>
        new(new FakeCurrentTenantProvider(TenantId), repository, new InMemoryIdentifierUniquenessRuleRepository(), sink, new InstitutionalIdentifierLockRegistry(),
            new IdentifierUniquenessRuleLockRegistry(), new FakePersonExistenceGateway());

    [Fact]
    public async Task Assign_records_exactly_one_allowed_audit_event_carrying_the_real_tenant_id_and_withholding_the_value()
    {
        var ct = TestContext.Current.CancellationToken;
        var sink = new InMemoryInstitutionalIdentifierAuditSink();
        var service = NewService(new InMemoryInstitutionalIdentifierRepository(), sink);

        await service.AssignAsync(PersonId, ValidRequest(), Actor, CanonicalId.NewEventId(), ct);

        var evt = Assert.Single(sink.Events);
        Assert.Equal("institutional_identifier.assign", evt.Action);
        Assert.Equal(AuditOutcome.Allowed, evt.Outcome);
        Assert.Equal("institutional_identifier", evt.Target.Type);
        Assert.Equal(TenantId, evt.TenantId);
        // The identifier VALUE is "personal" data (contracts/schemas/data-classification.json)
        // and must never appear in the audit trail, in Detail or otherwise - it is withheld,
        // never included-and-classified, so Detail's own highest classification stays
        // "internal" (type/custom_label/sequence_number), exactly mirroring BIO-IDN-001's
        // identical posture for name/date_of_birth.
        Assert.Contains("value", evt.Withheld);
        Assert.DoesNotContain(evt.Detail.Values, v => Equals(v, "MAT/2026/00123"));
        Assert.Equal("internal", evt.Classification);
    }

    [Fact]
    public async Task Retire_records_an_audit_event()
    {
        var ct = TestContext.Current.CancellationToken;
        var sink = new InMemoryInstitutionalIdentifierAuditSink();
        var service = NewService(new InMemoryInstitutionalIdentifierRepository(), sink);
        var identifier = await service.AssignAsync(PersonId, ValidRequest(), Actor, CanonicalId.NewEventId(), ct);

        await service.RetireAsync(PersonId, IdentifierType.MatriculationNumber, customLabel: null, sequenceNumber: 1,
            new RetireIdentifierRequest { Version = identifier.Version }, Actor, CanonicalId.NewEventId(), ct);

        var evt = sink.Events.Single(e => e.Action == "institutional_identifier.retire");
        Assert.Equal(AuditOutcome.Allowed, evt.Outcome);
    }

    [Fact]
    public async Task A_failed_validation_records_no_audit_event()
    {
        var ct = TestContext.Current.CancellationToken;
        var sink = new InMemoryInstitutionalIdentifierAuditSink();
        var service = NewService(new InMemoryInstitutionalIdentifierRepository(), sink);

        await Assert.ThrowsAsync<InstitutionalIdentifierError>(
            () => service.AssignAsync(PersonId, new AssignIdentifierRequest { Type = "NOT_A_TYPE", Value = "X" }, Actor, CanonicalId.NewEventId(), ct));

        Assert.Empty(sink.Events);
    }

    [Fact]
    public async Task An_already_active_attempt_records_no_audit_event()
    {
        var ct = TestContext.Current.CancellationToken;
        var sink = new InMemoryInstitutionalIdentifierAuditSink();
        var service = NewService(new InMemoryInstitutionalIdentifierRepository(), sink);
        await service.AssignAsync(PersonId, ValidRequest(), Actor, CanonicalId.NewEventId(), ct);
        var eventsAfterFirst = sink.Events.Count;

        await Assert.ThrowsAsync<InstitutionalIdentifierError>(() => service.AssignAsync(PersonId, ValidRequest("MAT/2026/99999"), Actor, CanonicalId.NewEventId(), ct));

        Assert.Equal(eventsAfterFirst, sink.Events.Count);
    }

    [Fact]
    public async Task Assign_is_never_committed_when_the_audit_sink_fails()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryInstitutionalIdentifierRepository();
        var service = NewService(repository, new ThrowingAuditSink());

        var error = await Assert.ThrowsAsync<InstitutionalIdentifierError>(() => service.AssignAsync(PersonId, ValidRequest(), Actor, CanonicalId.NewEventId(), ct));

        Assert.Equal("audit_unavailable", error.Code);
        Assert.Empty(await repository.ListByPersonAsync(TenantId, PersonId, ct));
    }

    [Fact]
    public async Task Retire_is_never_committed_when_the_audit_sink_fails()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryInstitutionalIdentifierRepository();
        var identifier = await NewService(repository, new InMemoryInstitutionalIdentifierAuditSink()).AssignAsync(PersonId, ValidRequest(), Actor, CanonicalId.NewEventId(), ct);
        var service = NewService(repository, new ThrowingAuditSink());

        var error = await Assert.ThrowsAsync<InstitutionalIdentifierError>(() => service.RetireAsync(
            PersonId, IdentifierType.MatriculationNumber, customLabel: null, sequenceNumber: 1,
            new RetireIdentifierRequest { Version = identifier.Version }, Actor, CanonicalId.NewEventId(), ct));

        Assert.Equal("audit_unavailable", error.Code);
        var stillStored = await repository.GetAsync(TenantId, PersonId, IdentifierType.MatriculationNumber, customLabel: null, sequenceNumber: 1, ct);
        Assert.Equal(IdentifierStatus.Active, stillStored!.Status);
    }

    [Fact]
    public async Task Two_genuinely_concurrent_retires_of_the_same_identifier_never_produce_a_false_allowed_audit()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryInstitutionalIdentifierRepository();
        var plainSink = new InMemoryInstitutionalIdentifierAuditSink();
        var lockRegistry = new InstitutionalIdentifierLockRegistry(); // shared: both callers must serialize
        var uniquenessLockRegistry = new IdentifierUniquenessRuleLockRegistry();
        var identifier = await new InstitutionalIdentifierService(
                new FakeCurrentTenantProvider(TenantId), repository, new InMemoryIdentifierUniquenessRuleRepository(), plainSink, lockRegistry, uniquenessLockRegistry,
                new FakePersonExistenceGateway())
            .AssignAsync(PersonId, ValidRequest(), Actor, CanonicalId.NewEventId(), ct);

        var delayingSink = new DelayFirstCallAuditSink(plainSink);
        var service = new InstitutionalIdentifierService(
            new FakeCurrentTenantProvider(TenantId), repository, new InMemoryIdentifierUniquenessRuleRepository(), delayingSink, lockRegistry, uniquenessLockRegistry,
            new FakePersonExistenceGateway());

        var taskA = TryRunAsync(() => service.RetireAsync(PersonId, IdentifierType.MatriculationNumber, customLabel: null, sequenceNumber: 1,
            new RetireIdentifierRequest { Version = identifier.Version }, Actor, CanonicalId.NewEventId(), ct));
        var taskB = TryRunAsync(() => service.RetireAsync(PersonId, IdentifierType.MatriculationNumber, customLabel: null, sequenceNumber: 1,
            new RetireIdentifierRequest { Version = identifier.Version }, Actor, CanonicalId.NewEventId(), ct));

        var (resultA, errorA) = await taskA;
        var (resultB, errorB) = await taskB;

        // The lock fully serializes the two callers. Both supplied the SAME (now-stale-for-
        // whichever-runs-second) expected version, so the second to run fails the version
        // check before it ever reaches the no-op "already retired" branch. Exactly one caller
        // succeeds, the other gets concurrency_conflict, and only ONE retire event was ever
        // recorded.
        var winner = resultA ?? resultB;
        var loserError = errorA ?? errorB;
        Assert.NotNull(winner);
        Assert.NotNull(loserError);
        Assert.Equal("concurrency_conflict", loserError!.Code);
        var retireEvents = plainSink.Events.Where(e => e.Action == "institutional_identifier.retire").ToList();
        Assert.Single(retireEvents);
    }

    [Fact]
    public async Task BIOIDN003_QA01_two_genuinely_concurrent_assigns_of_the_same_value_for_two_different_people_never_both_succeed()
    {
        // BIO-IDN-003-QA-01 (Seyi round 1, High): the per-person lock alone cannot serialize
        // two DIFFERENT people racing to assign the SAME enforced-unique value - only the
        // shared, tenant-wide (Type, CustomLabel) lock can. Both callers here target different
        // people (and therefore different InstitutionalIdentifierLockRegistry keys), so this
        // would have raced straight through before the fix; sharing the SAME
        // IdentifierUniquenessRuleLockRegistry instance between both callers, exactly as
        // production wiring shares one Singleton, is what proves the outer lock now serializes
        // them regardless.
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryInstitutionalIdentifierRepository();
        var ruleRepository = new InMemoryIdentifierUniquenessRuleRepository();
        var plainSink = new InMemoryInstitutionalIdentifierAuditSink();
        var lockRegistry = new InstitutionalIdentifierLockRegistry();
        var uniquenessLockRegistry = new IdentifierUniquenessRuleLockRegistry(); // shared: both callers for the same (type, custom_label) must serialize

        var delayingSink = new DelayFirstCallAuditSink(plainSink);
        var service = new InstitutionalIdentifierService(
            new FakeCurrentTenantProvider(TenantId), repository, ruleRepository, delayingSink, lockRegistry, uniquenessLockRegistry, new FakePersonExistenceGateway());

        var taskA = TryRunAsync(() => service.AssignAsync(PersonId, ValidRequest("MAT/2026/RACE"), Actor, CanonicalId.NewEventId(), ct));
        var taskB = TryRunAsync(() => service.AssignAsync(PersonId2, ValidRequest("MAT/2026/RACE"), Actor, CanonicalId.NewEventId(), ct));

        var (resultA, errorA) = await taskA;
        var (resultB, errorB) = await taskB;

        // The outer lock fully serializes the two callers even though they target different
        // people: whichever runs second reads the now-committed value and is correctly
        // rejected - exactly one succeeds, the other gets duplicate_identifier_value, and only
        // ONE record was ever stored across BOTH people.
        var winner = resultA ?? resultB;
        var loserError = errorA ?? errorB;
        Assert.NotNull(winner);
        Assert.NotNull(loserError);
        Assert.Equal("duplicate_identifier_value", loserError!.Code);
        var storedForA = await repository.ListByPersonAsync(TenantId, PersonId, ct);
        var storedForB = await repository.ListByPersonAsync(TenantId, PersonId2, ct);
        Assert.Equal(1, storedForA.Count + storedForB.Count);
        var assignEvents = plainSink.Events.Where(e => e.Action == "institutional_identifier.assign").ToList();
        Assert.Single(assignEvents);
    }

    [Fact]
    public async Task An_audit_write_that_durably_lands_but_throws_on_acknowledgement_still_commits_correctly()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new InMemoryInstitutionalIdentifierRepository();
        var sink = new InMemoryInstitutionalIdentifierAuditSink();
        var identifier = await NewService(repository, sink).AssignAsync(PersonId, ValidRequest(), Actor, CanonicalId.NewEventId(), ct);

        var service = NewService(repository, new DurablyWritesThenThrowsAuditSink(sink));
        var retired = await service.RetireAsync(PersonId, IdentifierType.MatriculationNumber, customLabel: null, sequenceNumber: 1,
            new RetireIdentifierRequest { Version = identifier.Version }, Actor, CanonicalId.NewEventId(), ct);

        Assert.Equal(IdentifierStatus.Retired, retired.Status);
    }

    [Fact]
    public async Task A_total_audit_store_outage_where_even_verification_fails_still_fails_closed()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = NewService(new InMemoryInstitutionalIdentifierRepository(), new ThrowingAuditSink());

        var error = await Assert.ThrowsAsync<InstitutionalIdentifierError>(() => service.AssignAsync(PersonId, ValidRequest(), Actor, CanonicalId.NewEventId(), ct));

        Assert.Equal("audit_unavailable", error.Code);
    }

    [Fact]
    public async Task A_cancellation_arriving_right_after_the_audit_write_succeeds_does_not_abort_the_commit()
    {
        var ct = TestContext.Current.CancellationToken;
        using var cts = new CancellationTokenSource();
        var repository = new ThrowsOnCancelledTokenRepository(new InMemoryInstitutionalIdentifierRepository());
        var sink = new CancelsCallerTokenAfterWriteAuditSink(new InMemoryInstitutionalIdentifierAuditSink(), cts);
        var service = NewService(repository, sink);

        await service.AssignAsync(PersonId, ValidRequest(), Actor, CanonicalId.NewEventId(), cts.Token);

        Assert.True(cts.IsCancellationRequested);
        Assert.NotNull(await repository.GetAsync(TenantId, PersonId, IdentifierType.MatriculationNumber, customLabel: null, sequenceNumber: 1, ct));
    }

    private static async Task<(InstitutionalIdentifier? Result, InstitutionalIdentifierError? Error)> TryRunAsync(Func<Task<InstitutionalIdentifier>> action)
    {
        try
        {
            return (await action(), null);
        }
        catch (InstitutionalIdentifierError ex)
        {
            return (null, ex);
        }
    }

    private sealed class ThrowingAuditSink : IInstitutionalIdentifierAuditSink
    {
        public Task WriteAsync(InstitutionalIdentifierAuditEvent auditEvent, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("synthetic sink outage");

        public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("synthetic sink outage: cannot even verify");
    }

    private sealed class DelayFirstCallAuditSink(IInstitutionalIdentifierAuditSink inner) : IInstitutionalIdentifierAuditSink
    {
        private int _calls;

        public async Task WriteAsync(InstitutionalIdentifierAuditEvent auditEvent, CancellationToken cancellationToken = default)
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

    private sealed class DurablyWritesThenThrowsAuditSink(IInstitutionalIdentifierAuditSink inner) : IInstitutionalIdentifierAuditSink
    {
        public async Task WriteAsync(InstitutionalIdentifierAuditEvent auditEvent, CancellationToken cancellationToken = default)
        {
            await inner.WriteAsync(auditEvent, cancellationToken);
            throw new InvalidOperationException("synthetic ack failure after a successful durable write");
        }

        public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) =>
            inner.ExistsAsync(eventId, cancellationToken);
    }

    private sealed class CancelsCallerTokenAfterWriteAuditSink(IInstitutionalIdentifierAuditSink inner, CancellationTokenSource cts) : IInstitutionalIdentifierAuditSink
    {
        public async Task WriteAsync(InstitutionalIdentifierAuditEvent auditEvent, CancellationToken cancellationToken = default)
        {
            await inner.WriteAsync(auditEvent, cancellationToken);
            cts.Cancel();
        }

        public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) =>
            inner.ExistsAsync(eventId, cancellationToken);
    }

    private sealed class ThrowsOnCancelledTokenRepository(IInstitutionalIdentifierRepository inner) : IInstitutionalIdentifierRepository
    {
        public Task<InstitutionalIdentifier> CreateAsync(InstitutionalIdentifier identifier, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return inner.CreateAsync(identifier, cancellationToken);
        }

        public Task<InstitutionalIdentifier?> GetActiveAsync(string tenantId, string personId, IdentifierType type, string? customLabel, CancellationToken cancellationToken = default) =>
            inner.GetActiveAsync(tenantId, personId, type, customLabel, cancellationToken);

        public Task<InstitutionalIdentifier?> GetAsync(string tenantId, string personId, IdentifierType type, string? customLabel, int sequenceNumber, CancellationToken cancellationToken = default) =>
            inner.GetAsync(tenantId, personId, type, customLabel, sequenceNumber, cancellationToken);

        public Task<IReadOnlyList<InstitutionalIdentifier>> ListHistoryAsync(string tenantId, string personId, IdentifierType type, string? customLabel, CancellationToken cancellationToken = default) =>
            inner.ListHistoryAsync(tenantId, personId, type, customLabel, cancellationToken);

        public Task<IReadOnlyList<InstitutionalIdentifier>> ListByPersonAsync(string tenantId, string personId, CancellationToken cancellationToken = default) =>
            inner.ListByPersonAsync(tenantId, personId, cancellationToken);

        public Task<bool> ValueExistsAsync(string tenantId, IdentifierType type, string? customLabel, string value, CancellationToken cancellationToken = default) =>
            inner.ValueExistsAsync(tenantId, type, customLabel, value, cancellationToken);

        public Task<bool> HasDuplicateValuesAsync(string tenantId, IdentifierType type, string? customLabel, CancellationToken cancellationToken = default) =>
            inner.HasDuplicateValuesAsync(tenantId, type, customLabel, cancellationToken);

        public Task<InstitutionalIdentifier> ReplaceAsync(InstitutionalIdentifier expected, InstitutionalIdentifier updated, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return inner.ReplaceAsync(expected, updated, cancellationToken);
        }
    }
}
