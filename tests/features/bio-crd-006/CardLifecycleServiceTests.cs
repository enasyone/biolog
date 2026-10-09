// Developer tests for BIO-CRD-006. SYNTHETIC fixtures only.
using Biolog.Features.CardLifecycle.Audit;
using Biolog.Features.CardLifecycle.Domain;
using Biolog.Features.CardLifecycle.Identifiers;
using Biolog.Features.CardLifecycle.Tenancy;
using Xunit;

namespace Biolog.Features.CardLifecycle.Tests;

public sealed class CardLifecycleServiceTests
{
    private const string TenantId = "ten_018f2e5a-1234-7abc-89ab-0123456789ab";
    private const string PersonId = "per_018f2e5a-9c3d-7a41-9b2e-4f7c8d1a6e5b";
    private const string CardClass = "staff";

    private static AuditActor Operator() => new(ActorType.User, "operator-1");

    private static (CardLifecycleService Service, InMemoryCardLifecycleRepository Repository, FakeCardIssueGateway Gateway, InMemoryCardRecoveryPolicyStore Policy) Build()
    {
        var repository = new InMemoryCardLifecycleRepository();
        var gateway = new FakeCardIssueGateway();
        var policy = new InMemoryCardRecoveryPolicyStore();
        var service = new CardLifecycleService(
            new FakeCurrentTenantProvider(TenantId), repository, gateway, policy,
            new Audit.InMemoryCardLifecycleAuditSink(), new InMemoryPresentationEventLog(),
            new CardLifecycleLockRegistry(), new InMemoryIdempotencyStore(), new FakeProductionStatusGateway(), new FakeCardActivationBarrier());
        return (service, repository, gateway, policy);
    }

    // --- AC01: report ---

    [Fact]
    public async Task Report_locks_a_card_and_records_the_configurable_reason()
    {
        var (service, _, gateway, _) = Build();
        gateway.Seed(PersonId, CardClass, 1);

        var record = await service.ReportAsync(PersonId, CardClass, 1, "stolen", "left in a taxi", expectedVersion: null, Operator(), CanonicalId.NewEventId(), CancellationToken.None);

        Assert.Equal(CardLockState.Locked, record.LockState);
        Assert.Equal("stolen", record.ReasonCode);
        Assert.Equal("left in a taxi", record.Reason);
        Assert.Equal(1, record.Version);
    }

    [Fact]
    public async Task Report_fails_when_the_underlying_card_issue_does_not_exist()
    {
        var (service, _, _, _) = Build();

        var error = await Assert.ThrowsAsync<CardLifecycleError>(() =>
            service.ReportAsync(PersonId, CardClass, 1, "lost", null, null, Operator(), CanonicalId.NewEventId(), CancellationToken.None));

        Assert.Equal("card_issue_not_found", error.Code);
    }

    [Fact]
    public async Task Report_rejects_a_blank_reason_code()
    {
        var (service, _, gateway, _) = Build();
        gateway.Seed(PersonId, CardClass, 1);

        var error = await Assert.ThrowsAsync<CardLifecycleError>(() =>
            service.ReportAsync(PersonId, CardClass, 1, "  ", null, null, Operator(), CanonicalId.NewEventId(), CancellationToken.None));

        Assert.Equal("validation_failed", error.Code);
    }

    [Fact]
    public async Task Report_a_second_time_while_already_Locked_is_refused()
    {
        var (service, _, gateway, _) = Build();
        gateway.Seed(PersonId, CardClass, 1);
        await service.ReportAsync(PersonId, CardClass, 1, "lost", null, null, Operator(), CanonicalId.NewEventId(), CancellationToken.None);

        var error = await Assert.ThrowsAsync<CardLifecycleError>(() =>
            service.ReportAsync(PersonId, CardClass, 1, "lost", null, 1, Operator(), CanonicalId.NewEventId(), CancellationToken.None));

        Assert.Equal("card_already_reported", error.Code);
    }

    [Fact]
    public async Task Re_reporting_an_existing_record_without_expected_version_is_428()
    {
        var (service, _, gateway, policy) = Build();
        gateway.Seed(PersonId, CardClass, 1);
        var reported = await service.ReportAsync(PersonId, CardClass, 1, "lost", null, null, Operator(), CanonicalId.NewEventId(), CancellationToken.None);
        await service.RecoverAsync(PersonId, CardClass, 1, reported.Version, Operator(), CanonicalId.NewEventId(), CancellationToken.None);

        var error = await Assert.ThrowsAsync<CardLifecycleError>(() =>
            service.ReportAsync(PersonId, CardClass, 1, "stolen", null, expectedVersion: null, Operator(), CanonicalId.NewEventId(), CancellationToken.None));

        Assert.Equal("precondition_required", error.Code);
        Assert.Equal(428, error.Status);
    }

    [Fact]
    public async Task Re_reporting_with_a_stale_expected_version_is_409()
    {
        var (service, _, gateway, _) = Build();
        gateway.Seed(PersonId, CardClass, 1);
        var reported = await service.ReportAsync(PersonId, CardClass, 1, "lost", null, null, Operator(), CanonicalId.NewEventId(), CancellationToken.None);
        await service.RecoverAsync(PersonId, CardClass, 1, reported.Version, Operator(), CanonicalId.NewEventId(), CancellationToken.None);

        var error = await Assert.ThrowsAsync<CardLifecycleError>(() =>
            service.ReportAsync(PersonId, CardClass, 1, "stolen", null, expectedVersion: reported.Version, Operator(), CanonicalId.NewEventId(), CancellationToken.None));

        Assert.Equal("concurrency_conflict", error.Code);
    }

    // --- AC02: recover ---

    [Fact]
    public async Task Recover_fails_when_no_record_was_ever_reported()
    {
        var (service, _, _, _) = Build();

        var error = await Assert.ThrowsAsync<CardLifecycleError>(() =>
            service.RecoverAsync(PersonId, CardClass, 1, 1, Operator(), CanonicalId.NewEventId(), CancellationToken.None));

        Assert.Equal("card_lifecycle_record_not_found", error.Code);
    }

    [Fact]
    public async Task Recover_transitions_Locked_to_Recovered_and_presentation_is_allowed_again()
    {
        var (service, _, gateway, _) = Build();
        gateway.Seed(PersonId, CardClass, 1);
        var reported = await service.ReportAsync(PersonId, CardClass, 1, "lost", null, null, Operator(), CanonicalId.NewEventId(), CancellationToken.None);

        var recovered = await service.RecoverAsync(PersonId, CardClass, 1, reported.Version, Operator(), CanonicalId.NewEventId(), CancellationToken.None);

        Assert.Equal(CardLockState.Recovered, recovered.LockState);
        Assert.NotNull(recovered.RecoveredAt);
        Assert.Equal("operator-1", recovered.RecoveredBy!.Id);

        var check = await service.CheckPresentationAsync(PersonId, CardClass, 1, null, Operator(), CancellationToken.None);
        Assert.Equal(PresentationOutcome.Allowed, check.Outcome);
    }

    [Fact]
    public async Task Recover_honours_tenant_policy_that_refuses_recovery_for_a_reason_code()
    {
        var (service, _, gateway, policy) = Build();
        gateway.Seed(PersonId, CardClass, 1);
        policy.SetPolicy(TenantId, "stolen", recoveryPermitted: false);
        var reported = await service.ReportAsync(PersonId, CardClass, 1, "stolen", null, null, Operator(), CanonicalId.NewEventId(), CancellationToken.None);

        var error = await Assert.ThrowsAsync<CardLifecycleError>(() =>
            service.RecoverAsync(PersonId, CardClass, 1, reported.Version, Operator(), CanonicalId.NewEventId(), CancellationToken.None));

        Assert.Equal("recovery_not_permitted", error.Code);
    }

    [Fact]
    public async Task Recover_of_a_card_that_was_never_Locked_is_refused()
    {
        var (service, _, gateway, _) = Build();
        gateway.Seed(PersonId, CardClass, 1);
        var reported = await service.ReportAsync(PersonId, CardClass, 1, "lost", null, null, Operator(), CanonicalId.NewEventId(), CancellationToken.None);
        var recovered = await service.RecoverAsync(PersonId, CardClass, 1, reported.Version, Operator(), CanonicalId.NewEventId(), CancellationToken.None);

        var error = await Assert.ThrowsAsync<CardLifecycleError>(() =>
            service.RecoverAsync(PersonId, CardClass, 1, recovered.Version, Operator(), CanonicalId.NewEventId(), CancellationToken.None));

        Assert.Equal("invalid_card_lifecycle_transition", error.Code);
    }

    [Fact]
    public async Task GetRecordAsync_returns_null_when_a_card_was_never_reported()
    {
        var (service, _, _, _) = Build();

        var record = await service.GetRecordAsync(PersonId, CardClass, 1, CancellationToken.None);

        Assert.Null(record);
    }
}
