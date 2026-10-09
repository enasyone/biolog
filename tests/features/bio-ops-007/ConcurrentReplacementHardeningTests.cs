using Biolog.Features.CardLifecycle;
using Biolog.Features.CardLifecycle.Audit;
using Biolog.Features.CardLifecycle.Domain;
using Biolog.Features.CardLifecycle.Identifiers;
using Biolog.Features.CardLifecycle.Tests;
using Xunit;

namespace Biolog.Features.PerformanceResilience.Tests;

public sealed class ConcurrentReplacementHardeningTests
{
    private const string TenantId = "ten_018f2e5a-1234-7abc-89ab-0123456789ab";
    private const string PersonId = "per_018f2e5a-9c3d-7a41-9b2e-4f7c8d1a6e5b";
    private const string CardClass = "staff";

    [Fact]
    public async Task AC01_two_distinct_replacements_racing_activation_leave_at_most_one_active_card()
    {
        var gateway = new FakeCardIssueGateway();
        var service = new CardLifecycleService(
            new FakeCurrentTenantProvider(TenantId),
            new InMemoryCardLifecycleRepository(),
            gateway,
            new InMemoryCardRecoveryPolicyStore(),
            new InMemoryCardLifecycleAuditSink(),
            new InMemoryPresentationEventLog(),
            new CardLifecycleLockRegistry(),
            new InMemoryIdempotencyStore(),
            new FakeProductionStatusGateway(),
            new FakeCardActivationBarrier());
        var actor = new AuditActor(ActorType.User, "synthetic-operator");
        var request = new ReplacementCardRequest { TemplateRef = "synthetic-template", PrinterRef = "synthetic-printer" };

        gateway.Seed(PersonId, CardClass, 1, CardIssueLifecycleStatus.Active);
        var second = await service.ReplaceAsync(PersonId, CardClass, request, actor, CanonicalId.NewEventId(), CancellationToken.None);
        var third = await service.ReplaceAsync(PersonId, CardClass, request, actor, CanonicalId.NewEventId(), CancellationToken.None);

        var attempts = await Task.WhenAll(
            Attempt(service, second, "idem-second", actor),
            Attempt(service, third, "idem-third", actor));

        var cards = await gateway.ListAsync(PersonId, CardClass, CancellationToken.None);
        Assert.Single(cards, card => card.Status == CardIssueLifecycleStatus.Active);
        Assert.Single(attempts, succeeded => succeeded);
    }

    private static async Task<bool> Attempt(
        CardLifecycleService service,
        CardIssueSnapshot replacement,
        string key,
        AuditActor actor)
    {
        try
        {
            await service.ActivateReplacementAsync(
                PersonId, CardClass, replacement.SequenceNumber, replacement.Version,
                key, actor, CanonicalId.NewEventId(), CancellationToken.None);
            return true;
        }
        catch (CardLifecycleError)
        {
            return false;
        }
    }

    [Fact]
    public async Task AC04_database_create_interruption_recovers_without_partial_or_duplicate_state()
    {
        var inner = new InMemoryCardLifecycleRepository();
        var database = new FailFirstCreateRepository(inner);
        var (service, gateway) = Build(database, new InMemoryPresentationEventLog());
        gateway.Seed(PersonId, CardClass, 1, CardIssueLifecycleStatus.Active);
        var actor = new AuditActor(ActorType.User, "synthetic-operator");

        await Assert.ThrowsAsync<IOException>(() => service.ReportAsync(
            PersonId, CardClass, 1, "lost", "synthetic database interruption", null,
            actor, CanonicalId.NewEventId(), CancellationToken.None));
        Assert.Null(inner.Get(TenantId, PersonId, CardClass, 1));

        var recovered = await service.ReportAsync(
            PersonId, CardClass, 1, "lost", "synthetic retry", null,
            actor, CanonicalId.NewEventId(), CancellationToken.None);
        Assert.Equal(CardLockState.Locked, recovered.LockState);
        Assert.Equal(1, recovered.Version);
        Assert.Equal(recovered, inner.Get(TenantId, PersonId, CardClass, 1));
    }

    [Fact]
    public async Task AC04_queue_append_interruption_retries_without_loss_or_duplicate_event()
    {
        var queue = new FailFirstAppendEventLog(new InMemoryPresentationEventLog());
        var (service, _) = Build(new InMemoryCardLifecycleRepository(), queue);
        var actor = new AuditActor(ActorType.Device, "synthetic-reader");
        const string idempotencyKey = "synthetic-queue-retry";

        var first = await Assert.ThrowsAsync<CardLifecycleError>(() => service.IntakePresentationEventAsync(
            PersonId, CardClass, 1, PresentationOutcome.Denied, "lost",
            "2026-10-08T04:00:00.000Z", "synthetic-reader", idempotencyKey,
            actor, CancellationToken.None));
        Assert.Equal("audit_unavailable", first.Code);
        Assert.Empty(await queue.ListAsync(TenantId, PersonId, CardClass, 1, TestContext.Current.CancellationToken));

        var recovered = await service.IntakePresentationEventAsync(
            PersonId, CardClass, 1, PresentationOutcome.Denied, "lost",
            "2026-10-08T04:00:00.000Z", "synthetic-reader", idempotencyKey,
            actor, CancellationToken.None);
        var replay = await service.IntakePresentationEventAsync(
            PersonId, CardClass, 1, PresentationOutcome.Denied, "lost",
            "2026-10-08T04:00:00.000Z", "synthetic-reader", idempotencyKey,
            actor, CancellationToken.None);

        Assert.Equal(recovered.EventId, replay.EventId);
        Assert.Single(await queue.ListAsync(TenantId, PersonId, CardClass, 1, TestContext.Current.CancellationToken));
    }

    private static (CardLifecycleService Service, FakeCardIssueGateway Gateway) Build(
        ICardLifecycleRepository repository,
        IPresentationEventLog eventLog)
    {
        var gateway = new FakeCardIssueGateway();
        return (new CardLifecycleService(
            new FakeCurrentTenantProvider(TenantId), repository, gateway,
            new InMemoryCardRecoveryPolicyStore(), new InMemoryCardLifecycleAuditSink(), eventLog,
            new CardLifecycleLockRegistry(), new InMemoryIdempotencyStore(),
            new FakeProductionStatusGateway(), new FakeCardActivationBarrier()), gateway);
    }

    private sealed class FailFirstCreateRepository(ICardLifecycleRepository inner) : ICardLifecycleRepository
    {
        private bool _fail = true;
        public CardLifecycleRecord? Get(string tenantId, string personId, string cardClass, int sequenceNumber) =>
            inner.Get(tenantId, personId, cardClass, sequenceNumber);
        public CardLifecycleRecord Create(CardLifecycleRecord record)
        {
            if (_fail)
            {
                _fail = false;
                throw new IOException("synthetic database write interruption");
            }
            return inner.Create(record);
        }
        public CardLifecycleRecord Replace(CardLifecycleRecord expected, CardLifecycleRecord updated) => inner.Replace(expected, updated);
        public IReadOnlyList<CardLifecycleRecord> ListDenyList(string tenantId, string? since) => inner.ListDenyList(tenantId, since);
    }

    private sealed class FailFirstAppendEventLog(IPresentationEventLog inner) : IPresentationEventLog
    {
        private bool _fail = true;
        public Task AppendAsync(PresentationEvent presentationEvent, CancellationToken cancellationToken = default)
        {
            if (_fail)
            {
                _fail = false;
                throw new IOException("synthetic queue append interruption");
            }
            return inner.AppendAsync(presentationEvent, cancellationToken);
        }
        public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) => inner.ExistsAsync(eventId, cancellationToken);
        public Task<IReadOnlyList<PresentationEvent>> ListAsync(string tenantId, string personId, string cardClass, int sequenceNumber, CancellationToken cancellationToken = default) =>
            inner.ListAsync(tenantId, personId, cardClass, sequenceNumber, cancellationToken);
    }
}
