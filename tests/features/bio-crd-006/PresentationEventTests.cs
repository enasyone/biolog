// Developer tests for BIO-CRD-006. SYNTHETIC fixtures only.
using Biolog.Features.CardLifecycle.Audit;
using Biolog.Features.CardLifecycle.Domain;
using Biolog.Features.CardLifecycle.Identifiers;
using Xunit;

namespace Biolog.Features.CardLifecycle.Tests;

/// <summary>AC05: old-card presentation denies and logs, even when offline once revocation is available locally.</summary>
public sealed class PresentationEventTests
{
    private const string TenantId = "ten_018f2e5a-1234-7abc-89ab-0123456789ab";
    private const string PersonId = "per_018f2e5a-9c3d-7a41-9b2e-4f7c8d1a6e5b";
    private const string CardClass = "staff";

    private static AuditActor Operator() => new(ActorType.User, "operator-1");

    private static (CardLifecycleService Service, InMemoryCardLifecycleRepository Repository, FakeCardIssueGateway Gateway, InMemoryPresentationEventLog Log) Build()
    {
        var repository = new InMemoryCardLifecycleRepository();
        var gateway = new FakeCardIssueGateway();
        var log = new InMemoryPresentationEventLog();
        var service = new CardLifecycleService(
            new FakeCurrentTenantProvider(TenantId), repository, gateway, new InMemoryCardRecoveryPolicyStore(),
            new Audit.InMemoryCardLifecycleAuditSink(), log,
            new CardLifecycleLockRegistry(), new InMemoryIdempotencyStore(), new FakeProductionStatusGateway(), new FakeCardActivationBarrier());
        return (service, repository, gateway, log);
    }

    [Fact]
    public async Task CheckPresentation_allows_a_card_that_was_never_reported()
    {
        var (service, _, gateway, _) = Build();
        gateway.Seed(PersonId, CardClass, 1);

        var result = await service.CheckPresentationAsync(PersonId, CardClass, 1, null, Operator(), CancellationToken.None);

        Assert.Equal(PresentationOutcome.Allowed, result.Outcome);
    }

    [Fact]
    public async Task CheckPresentation_denies_and_logs_a_Locked_card_immediately_AC01()
    {
        var (service, _, gateway, log) = Build();
        gateway.Seed(PersonId, CardClass, 1);
        await service.ReportAsync(PersonId, CardClass, 1, "stolen", null, null, Operator(), CanonicalId.NewEventId(), CancellationToken.None);

        var result = await service.CheckPresentationAsync(PersonId, CardClass, 1, "reader-42", Operator(), CancellationToken.None);

        Assert.Equal(PresentationOutcome.Denied, result.Outcome);
        Assert.Equal("stolen", result.ReasonCode);

        var events = await log.ListAsync(TenantId, PersonId, CardClass, 1, CancellationToken.None);
        Assert.Single(events);
        Assert.Equal(PresentationOutcome.Denied, events[0].Outcome);
        Assert.Equal("reader-42", events[0].DeviceRef);
        Assert.Equal("online_check", events[0].Source);
        Assert.Equal("operator-1", events[0].RecordedBy.Id); // who/what recorded the check, not just the device it happened on
    }

    [Fact]
    public async Task CheckPresentation_denies_a_PermanentlyRevoked_card_after_a_replacement_activated_AC04()
    {
        var (service, _, gateway, _) = Build();
        gateway.Seed(PersonId, CardClass, 1, CardIssueLifecycleStatus.Active);
        var replacement = await service.ReplaceAsync(PersonId, CardClass, new ReplacementCardRequest { TemplateRef = "t", PrinterRef = "p" }, Operator(), CanonicalId.NewEventId(), CancellationToken.None);
        await service.ActivateReplacementAsync(PersonId, CardClass, replacement.SequenceNumber, replacement.Version, "idem-1", Operator(), CanonicalId.NewEventId(), CancellationToken.None);

        var result = await service.CheckPresentationAsync(PersonId, CardClass, 1, null, Operator(), CancellationToken.None);

        Assert.Equal(PresentationOutcome.Denied, result.Outcome);
        Assert.Equal("superseded_by_replacement", result.ReasonCode);
    }

    // --- AC05 offline sync intake (ADR-0004 §4.3) ---

    [Fact]
    public async Task IntakePresentationEvent_requires_an_idempotency_key()
    {
        var (service, _, _, _) = Build();

        var error = await Assert.ThrowsAsync<CardLifecycleError>(() =>
            service.IntakePresentationEventAsync(PersonId, CardClass, 1, PresentationOutcome.Denied, "stolen", "2026-01-01T00:00:00.000Z", "reader-9", idempotencyKey: "", Operator(), CancellationToken.None));

        Assert.Equal("idempotency_key_required", error.Code);
    }

    [Fact]
    public async Task IntakePresentationEvent_durably_logs_a_decision_an_offline_reader_already_made_locally()
    {
        var (service, _, _, log) = Build();

        var evt = await service.IntakePresentationEventAsync(
            PersonId, CardClass, 1, PresentationOutcome.Denied, "stolen", "2026-01-01T08:00:00.000Z", "reader-offline-1", "idem-offline-1", Operator(), CancellationToken.None);

        Assert.Equal("offline_sync", evt.Source);
        Assert.Equal("2026-01-01T08:00:00.000Z", evt.OccurredAt); // the reader's own recorded time, not now
        var events = await log.ListAsync(TenantId, PersonId, CardClass, 1, CancellationToken.None);
        Assert.Single(events);
    }

    [Fact]
    public async Task IntakePresentationEvent_replaying_the_same_idempotency_key_does_not_double_log()
    {
        var (service, _, _, log) = Build();

        await service.IntakePresentationEventAsync(PersonId, CardClass, 1, PresentationOutcome.Denied, "stolen", "2026-01-01T08:00:00.000Z", null, "idem-1", Operator(), CancellationToken.None);
        await service.IntakePresentationEventAsync(PersonId, CardClass, 1, PresentationOutcome.Denied, "stolen", "2026-01-01T08:00:00.000Z", null, "idem-1", Operator(), CancellationToken.None);

        var events = await log.ListAsync(TenantId, PersonId, CardClass, 1, CancellationToken.None);
        Assert.Single(events);
    }

    [Fact]
    public async Task IntakePresentationEvent_reusing_an_idempotency_key_with_different_content_is_rejected()
    {
        var (service, _, _, _) = Build();
        await service.IntakePresentationEventAsync(PersonId, CardClass, 1, PresentationOutcome.Denied, "stolen", "2026-01-01T08:00:00.000Z", null, "idem-1", Operator(), CancellationToken.None);

        var error = await Assert.ThrowsAsync<CardLifecycleError>(() =>
            service.IntakePresentationEventAsync(PersonId, CardClass, 2, PresentationOutcome.Denied, "stolen", "2026-01-01T08:00:00.000Z", null, "idem-1", Operator(), CancellationToken.None));

        Assert.Equal("idempotency_key_reuse", error.Code);
    }

    [Fact]
    public async Task IntakePresentationEvent_does_not_require_the_card_issue_to_exist_on_this_server_an_offline_reader_may_be_ahead()
    {
        // The whole point of AC05 is that the reader denied using ONLY its locally cached deny
        // list - re-validating against this server's live state here would defeat "even when
        // offline", so intake never calls the card-issue gateway at all.
        var (service, _, _, _) = Build();

        var evt = await service.IntakePresentationEventAsync(
            PersonId, CardClass, 999, PresentationOutcome.Denied, "stolen", "2026-01-01T08:00:00.000Z", null, "idem-1", Operator(), CancellationToken.None);

        Assert.Equal(999, evt.SequenceNumber);
    }

    // --- AC05 deny-list feed ---

    [Fact]
    public async Task DenyList_includes_a_Locked_card_so_an_offline_reader_can_cache_it_before_going_offline()
    {
        var (service, _, gateway, _) = Build();
        gateway.Seed(PersonId, CardClass, 1);
        await service.ReportAsync(PersonId, CardClass, 1, "lost", null, null, Operator(), CanonicalId.NewEventId(), CancellationToken.None);

        var denyList = await service.GetDenyListAsync(since: null, cancellationToken: CancellationToken.None);

        Assert.Single(denyList);
        Assert.Equal(1, denyList[0].SequenceNumber);
    }

    [Fact]
    public async Task DenyList_excludes_a_Recovered_card()
    {
        var (service, _, gateway, _) = Build();
        gateway.Seed(PersonId, CardClass, 1);
        var reported = await service.ReportAsync(PersonId, CardClass, 1, "lost", null, null, Operator(), CanonicalId.NewEventId(), CancellationToken.None);
        await service.RecoverAsync(PersonId, CardClass, 1, reported.Version, Operator(), CanonicalId.NewEventId(), CancellationToken.None);

        var denyList = await service.GetDenyListAsync(since: null, cancellationToken: CancellationToken.None);

        Assert.Empty(denyList);
    }
}
