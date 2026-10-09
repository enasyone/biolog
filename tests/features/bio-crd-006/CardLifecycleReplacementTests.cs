// Developer tests for BIO-CRD-006. SYNTHETIC fixtures only.
using Biolog.Features.CardLifecycle.Audit;
using Biolog.Features.CardLifecycle.Domain;
using Biolog.Features.CardLifecycle.Identifiers;
using Xunit;

namespace Biolog.Features.CardLifecycle.Tests;

/// <summary>AC03 (replacement uses next sequence) and AC04 (activation permanently revokes every earlier card).</summary>
public sealed class CardLifecycleReplacementTests
{
    private const string TenantId = "ten_018f2e5a-1234-7abc-89ab-0123456789ab";
    private const string PersonId = "per_018f2e5a-9c3d-7a41-9b2e-4f7c8d1a6e5b";
    private const string CardClass = "staff";

    private static AuditActor Operator() => new(ActorType.User, "operator-1");

    private static ReplacementCardRequest Request() => new() { TemplateRef = "template-1", PrinterRef = "printer-1" };

    private static (CardLifecycleService Service, InMemoryCardLifecycleRepository Repository, FakeCardIssueGateway Gateway) Build()
    {
        var repository = new InMemoryCardLifecycleRepository();
        var gateway = new FakeCardIssueGateway();
        var service = new CardLifecycleService(
            new FakeCurrentTenantProvider(TenantId), repository, gateway, new InMemoryCardRecoveryPolicyStore(),
            new Audit.InMemoryCardLifecycleAuditSink(), new InMemoryPresentationEventLog(),
            new CardLifecycleLockRegistry(), new InMemoryIdempotencyStore(), new FakeProductionStatusGateway(), new FakeCardActivationBarrier());
        return (service, repository, gateway);
    }

    [Fact]
    public async Task Replace_uses_the_next_sequence_number()
    {
        var (service, _, gateway) = Build();
        gateway.Seed(PersonId, CardClass, 1, CardIssueLifecycleStatus.Active);

        var replacement = await service.ReplaceAsync(PersonId, CardClass, Request(), Operator(), CanonicalId.NewEventId(), CancellationToken.None);

        Assert.Equal(2, replacement.SequenceNumber);
    }

    [Fact]
    public async Task Replace_can_be_called_repeatedly_and_each_call_advances_the_sequence()
    {
        var (service, _, gateway) = Build();
        gateway.Seed(PersonId, CardClass, 1, CardIssueLifecycleStatus.Active);

        var second = await service.ReplaceAsync(PersonId, CardClass, Request(), Operator(), CanonicalId.NewEventId(), CancellationToken.None);
        var third = await service.ReplaceAsync(PersonId, CardClass, Request(), Operator(), CanonicalId.NewEventId(), CancellationToken.None);

        Assert.Equal(2, second.SequenceNumber);
        Assert.Equal(3, third.SequenceNumber);
    }

    [Fact]
    public async Task ActivateReplacement_requires_an_idempotency_key()
    {
        var (service, _, gateway) = Build();
        gateway.Seed(PersonId, CardClass, 1, CardIssueLifecycleStatus.Active);
        var replacement = await service.ReplaceAsync(PersonId, CardClass, Request(), Operator(), CanonicalId.NewEventId(), CancellationToken.None);

        var error = await Assert.ThrowsAsync<CardLifecycleError>(() =>
            service.ActivateReplacementAsync(PersonId, CardClass, replacement.SequenceNumber, replacement.Version, idempotencyKey: "", Operator(), CanonicalId.NewEventId(), CancellationToken.None));

        Assert.Equal("idempotency_key_required", error.Code);
    }

    [Fact]
    public async Task ActivateReplacement_activates_the_new_card_and_permanently_revokes_the_one_earlier_active_card()
    {
        var (service, repository, gateway) = Build();
        gateway.Seed(PersonId, CardClass, 1, CardIssueLifecycleStatus.Active);
        var replacement = await service.ReplaceAsync(PersonId, CardClass, Request(), Operator(), CanonicalId.NewEventId(), CancellationToken.None);

        var activated = await service.ActivateReplacementAsync(
            PersonId, CardClass, replacement.SequenceNumber, replacement.Version, "idem-1", Operator(), CanonicalId.NewEventId(), CancellationToken.None);

        Assert.Equal(CardIssueLifecycleStatus.Active, activated.Status);

        var earlier = repository.Get(TenantId, PersonId, CardClass, 1);
        Assert.NotNull(earlier);
        Assert.Equal(CardLockState.PermanentlyRevoked, earlier!.LockState);
        Assert.Equal("superseded_by_replacement", earlier.RevokedReasonCode);

        var earlierIssue = await gateway.GetAsync(PersonId, CardClass, 1, CancellationToken.None);
        Assert.Equal(CardIssueLifecycleStatus.Deactivated, earlierIssue!.Status);
    }

    [Fact]
    public async Task ActivateReplacement_permanently_revokes_every_earlier_card_not_only_the_active_one()
    {
        var (service, repository, gateway) = Build();
        // Sequence 1 was already retired by an earlier lifecycle event and is Issued-but-never-activated
        // sequence 2 is a lost-and-reported card that is still Active; sequence 3 is the new replacement.
        gateway.Seed(PersonId, CardClass, 1, CardIssueLifecycleStatus.Issued);
        gateway.Seed(PersonId, CardClass, 2, CardIssueLifecycleStatus.Active);
        var replacement = await service.ReplaceAsync(PersonId, CardClass, Request(), Operator(), CanonicalId.NewEventId(), CancellationToken.None);
        Assert.Equal(3, replacement.SequenceNumber);

        await service.ActivateReplacementAsync(PersonId, CardClass, 3, replacement.Version, "idem-1", Operator(), CanonicalId.NewEventId(), CancellationToken.None);

        Assert.Equal(CardLockState.PermanentlyRevoked, repository.Get(TenantId, PersonId, CardClass, 1)!.LockState);
        Assert.Equal(CardLockState.PermanentlyRevoked, repository.Get(TenantId, PersonId, CardClass, 2)!.LockState);
    }

    [Fact]
    public async Task ActivateReplacement_permanently_revokes_a_card_that_was_previously_recovered()
    {
        var (service, repository, gateway) = Build();
        gateway.Seed(PersonId, CardClass, 1, CardIssueLifecycleStatus.Active);
        var reported = await service.ReportAsync(PersonId, CardClass, 1, "lost", null, null, Operator(), CanonicalId.NewEventId(), CancellationToken.None);
        var recovered = await service.RecoverAsync(PersonId, CardClass, 1, reported.Version, Operator(), CanonicalId.NewEventId(), CancellationToken.None);
        Assert.Equal(CardLockState.Recovered, recovered.LockState);

        var replacement = await service.ReplaceAsync(PersonId, CardClass, Request(), Operator(), CanonicalId.NewEventId(), CancellationToken.None);
        await service.ActivateReplacementAsync(PersonId, CardClass, replacement.SequenceNumber, replacement.Version, "idem-1", Operator(), CanonicalId.NewEventId(), CancellationToken.None);

        var record = repository.Get(TenantId, PersonId, CardClass, 1);
        Assert.Equal(CardLockState.PermanentlyRevoked, record!.LockState);
    }

    [Fact]
    public async Task After_permanent_revocation_recover_is_refused_even_though_it_was_once_Locked()
    {
        var (service, _, gateway) = Build();
        gateway.Seed(PersonId, CardClass, 1, CardIssueLifecycleStatus.Active);
        var reported = await service.ReportAsync(PersonId, CardClass, 1, "lost", null, null, Operator(), CanonicalId.NewEventId(), CancellationToken.None);

        var replacement = await service.ReplaceAsync(PersonId, CardClass, Request(), Operator(), CanonicalId.NewEventId(), CancellationToken.None);
        await service.ActivateReplacementAsync(PersonId, CardClass, replacement.SequenceNumber, replacement.Version, "idem-1", Operator(), CanonicalId.NewEventId(), CancellationToken.None);

        var revoked = await service.GetRecordAsync(PersonId, CardClass, 1, CancellationToken.None);
        var error = await Assert.ThrowsAsync<CardLifecycleError>(() =>
            service.RecoverAsync(PersonId, CardClass, 1, revoked!.Version, Operator(), CanonicalId.NewEventId(), CancellationToken.None));

        Assert.Equal("invalid_card_lifecycle_transition", error.Code);
    }

    [Fact]
    public async Task Replaying_the_same_activate_replacement_idempotency_key_returns_the_original_result_without_double_revoking()
    {
        var (service, repository, gateway) = Build();
        gateway.Seed(PersonId, CardClass, 1, CardIssueLifecycleStatus.Active);
        var replacement = await service.ReplaceAsync(PersonId, CardClass, Request(), Operator(), CanonicalId.NewEventId(), CancellationToken.None);

        var first = await service.ActivateReplacementAsync(PersonId, CardClass, replacement.SequenceNumber, replacement.Version, "idem-1", Operator(), CanonicalId.NewEventId(), CancellationToken.None);
        var second = await service.ActivateReplacementAsync(PersonId, CardClass, replacement.SequenceNumber, replacement.Version, "idem-1", Operator(), CanonicalId.NewEventId(), CancellationToken.None);

        Assert.Equal(first, second);
        var record = repository.Get(TenantId, PersonId, CardClass, 1);
        Assert.Equal(CardLockState.PermanentlyRevoked, record!.LockState);
        Assert.Equal(2, record.Version); // begun (v1) then confirmed (v2) once by the first call; the replay short-circuits at this story's own idempotency layer before ever touching the sweep again
    }

    [Fact]
    public async Task Concurrent_activation_attempts_for_the_same_replacement_never_both_succeed()
    {
        var (service, _, gateway) = Build();
        gateway.Seed(PersonId, CardClass, 1, CardIssueLifecycleStatus.Active);
        var replacement = await service.ReplaceAsync(PersonId, CardClass, Request(), Operator(), CanonicalId.NewEventId(), CancellationToken.None);

        var attempt1 = service.ActivateReplacementAsync(PersonId, CardClass, replacement.SequenceNumber, replacement.Version, "idem-a", Operator(), CanonicalId.NewEventId(), CancellationToken.None);
        var attempt2 = service.ActivateReplacementAsync(PersonId, CardClass, replacement.SequenceNumber, replacement.Version, "idem-b", Operator(), CanonicalId.NewEventId(), CancellationToken.None);

        var results = await Task.WhenAll(attempt1.ContinueWith(t => t.IsCompletedSuccessfully), attempt2.ContinueWith(t => t.IsCompletedSuccessfully));
        Assert.Single(results, ok => ok);
    }
}
