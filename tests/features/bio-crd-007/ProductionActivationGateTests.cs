// Developer tests for BIO-CRD-007. SYNTHETIC fixtures only.
using Biolog.Features.CardProduction.Audit;
using Biolog.Features.CardProduction.Domain;
using Biolog.Features.CardProduction.Identifiers;
using Xunit;

namespace Biolog.Features.CardProduction.Tests;

/// <summary>AC02 (print/encode/read-back/QC failures do not activate a credential) and AC05 (activation preserves the one-active-card invariant).</summary>
public sealed class ProductionActivationGateTests
{
    private const string TenantId = "ten_018f2e5a-1234-7abc-89ab-0123456789ab";
    private const string PersonId = "per_018f2e5a-9c3d-7a41-9b2e-4f7c8d1a6e5b";
    private const string CardClass = "staff";

    private static AuditActor Operator() => new(ActorType.User, "operator-1");

    private static (ProductionService Service, FakeCardIssueGateway Gateway) Build()
    {
        var gateway = new FakeCardIssueGateway();
        var service = new ProductionService(
            new FakeCurrentTenantProvider(TenantId), new InMemoryProductionJobRepository(), new InMemoryProductionPackageStore(), new InMemoryProductionSigningKeyStore(),
            gateway, new InMemoryProductionAuditSink(), new ProductionJobLockRegistry(), new InMemoryIdempotencyStore(), new FakeCardActivationBarrier());
        return (service, gateway);
    }

    [Fact]
    public async Task Activation_is_refused_when_no_production_job_has_ever_been_created()
    {
        var (service, gateway) = Build();
        gateway.Seed(PersonId, CardClass, 1);

        var error = await Assert.ThrowsAsync<ProductionError>(() =>
            service.ActivateAfterProductionAsync(PersonId, CardClass, 1, 1, "idem-1", Operator(), CanonicalId.NewEventId(), CancellationToken.None));

        Assert.Equal("production_not_passed", error.Code);
        // The real gateway's own ActivateAsync must never even be reached - it would have thrown
        // card_issue_not_found for a DIFFERENT reason if it had (no, it exists) - the assertion
        // that matters is the card issue itself is untouched, still Issued, not Active.
        var snapshot = await gateway.GetAsync(PersonId, CardClass, 1, CancellationToken.None);
        Assert.Equal(CardIssueLifecycleStatus.Issued, snapshot!.Status);
    }

    [Fact]
    public async Task Activation_is_refused_when_the_latest_job_Failed()
    {
        var (service, gateway) = Build();
        gateway.Seed(PersonId, CardClass, 1);
        var job = await service.CreateJobAsync(PersonId, CardClass, 1, "printer-1", "workstation-1", null, Operator(), CanonicalId.NewEventId(), CancellationToken.None);
        var afterPrint = await service.RecordPrintAsync(PersonId, CardClass, 1, 1, false, "ribbon jam", job.Version, Operator(), CanonicalId.NewEventId(), CancellationToken.None);
        var afterEncode = await service.RecordEncodeAsync(PersonId, CardClass, 1, 1, true, null, afterPrint.Version, Operator(), CanonicalId.NewEventId(), CancellationToken.None);
        var failed = await service.RecordReadBackQcAsync(PersonId, CardClass, 1, 1, true, null, afterEncode.Version, Operator(), CanonicalId.NewEventId(), CancellationToken.None);
        Assert.Equal(ProductionJobStatus.Failed, failed.Status);

        var error = await Assert.ThrowsAsync<ProductionError>(() =>
            service.ActivateAfterProductionAsync(PersonId, CardClass, 1, 1, "idem-1", Operator(), CanonicalId.NewEventId(), CancellationToken.None));

        Assert.Equal("production_not_passed", error.Code);
        Assert.Equal(CardIssueLifecycleStatus.Issued, (await gateway.GetAsync(PersonId, CardClass, 1, CancellationToken.None))!.Status);
    }

    [Fact]
    public async Task Activation_is_refused_when_the_latest_job_was_Rejected_even_though_it_mechanically_Passed()
    {
        var (service, gateway) = Build();
        gateway.Seed(PersonId, CardClass, 1);
        var job = await service.CreateJobAsync(PersonId, CardClass, 1, "printer-1", "workstation-1", null, Operator(), CanonicalId.NewEventId(), CancellationToken.None);
        var afterPrint = await service.RecordPrintAsync(PersonId, CardClass, 1, 1, true, null, job.Version, Operator(), CanonicalId.NewEventId(), CancellationToken.None);
        var afterEncode = await service.RecordEncodeAsync(PersonId, CardClass, 1, 1, true, null, afterPrint.Version, Operator(), CanonicalId.NewEventId(), CancellationToken.None);
        var passed = await service.RecordReadBackQcAsync(PersonId, CardClass, 1, 1, true, null, afterEncode.Version, Operator(), CanonicalId.NewEventId(), CancellationToken.None);
        var rejected = await service.RejectJobAsync(PersonId, CardClass, 1, 1, "visible scratch", passed.Version, Operator(), CanonicalId.NewEventId(), CancellationToken.None);
        Assert.Equal(ProductionJobStatus.Rejected, rejected.Status);

        var error = await Assert.ThrowsAsync<ProductionError>(() =>
            service.ActivateAfterProductionAsync(PersonId, CardClass, 1, 1, "idem-1", Operator(), CanonicalId.NewEventId(), CancellationToken.None));

        Assert.Equal("production_not_passed", error.Code);
    }

    [Fact]
    public async Task Activation_is_refused_while_the_latest_job_is_still_InProgress()
    {
        var (service, gateway) = Build();
        gateway.Seed(PersonId, CardClass, 1);
        await service.CreateJobAsync(PersonId, CardClass, 1, "printer-1", "workstation-1", null, Operator(), CanonicalId.NewEventId(), CancellationToken.None);

        var error = await Assert.ThrowsAsync<ProductionError>(() =>
            service.ActivateAfterProductionAsync(PersonId, CardClass, 1, 1, "idem-1", Operator(), CanonicalId.NewEventId(), CancellationToken.None));

        Assert.Equal("production_not_passed", error.Code);
    }

    [Fact]
    public async Task Activation_succeeds_and_delegates_to_the_real_gateway_once_the_latest_job_Passed()
    {
        var (service, gateway) = Build();
        gateway.Seed(PersonId, CardClass, 1);
        var job = await service.CreateJobAsync(PersonId, CardClass, 1, "printer-1", "workstation-1", null, Operator(), CanonicalId.NewEventId(), CancellationToken.None);
        var afterPrint = await service.RecordPrintAsync(PersonId, CardClass, 1, 1, true, null, job.Version, Operator(), CanonicalId.NewEventId(), CancellationToken.None);
        var afterEncode = await service.RecordEncodeAsync(PersonId, CardClass, 1, 1, true, null, afterPrint.Version, Operator(), CanonicalId.NewEventId(), CancellationToken.None);
        var passed = await service.RecordReadBackQcAsync(PersonId, CardClass, 1, 1, true, null, afterEncode.Version, Operator(), CanonicalId.NewEventId(), CancellationToken.None);
        Assert.Equal(ProductionJobStatus.Passed, passed.Status);

        var activated = await service.ActivateAfterProductionAsync(PersonId, CardClass, 1, 1, "idem-1", Operator(), CanonicalId.NewEventId(), CancellationToken.None);

        Assert.Equal(CardIssueLifecycleStatus.Active, activated.Status);
    }

    [Fact]
    public async Task Activation_requires_Idempotency_Key()
    {
        var (service, gateway) = Build();
        gateway.Seed(PersonId, CardClass, 1);
        var job = await service.CreateJobAsync(PersonId, CardClass, 1, "printer-1", "workstation-1", null, Operator(), CanonicalId.NewEventId(), CancellationToken.None);
        var afterPrint = await service.RecordPrintAsync(PersonId, CardClass, 1, 1, true, null, job.Version, Operator(), CanonicalId.NewEventId(), CancellationToken.None);
        var afterEncode = await service.RecordEncodeAsync(PersonId, CardClass, 1, 1, true, null, afterPrint.Version, Operator(), CanonicalId.NewEventId(), CancellationToken.None);
        await service.RecordReadBackQcAsync(PersonId, CardClass, 1, 1, true, null, afterEncode.Version, Operator(), CanonicalId.NewEventId(), CancellationToken.None);

        var error = await Assert.ThrowsAsync<ProductionError>(() =>
            service.ActivateAfterProductionAsync(PersonId, CardClass, 1, 1, "", Operator(), CanonicalId.NewEventId(), CancellationToken.None));

        Assert.Equal("idempotency_key_required", error.Code);
    }

    [Fact]
    public async Task AC05_the_real_gateways_own_one_active_card_invariant_still_refuses_a_second_activation()
    {
        var (service, gateway) = Build();
        gateway.Seed(PersonId, CardClass, 1);
        gateway.Seed(PersonId, CardClass, 2, CardIssueLifecycleStatus.Active); // already an active card in this class

        var job = await service.CreateJobAsync(PersonId, CardClass, 1, "printer-1", "workstation-1", null, Operator(), CanonicalId.NewEventId(), CancellationToken.None);
        var afterPrint = await service.RecordPrintAsync(PersonId, CardClass, 1, 1, true, null, job.Version, Operator(), CanonicalId.NewEventId(), CancellationToken.None);
        var afterEncode = await service.RecordEncodeAsync(PersonId, CardClass, 1, 1, true, null, afterPrint.Version, Operator(), CanonicalId.NewEventId(), CancellationToken.None);
        await service.RecordReadBackQcAsync(PersonId, CardClass, 1, 1, true, null, afterEncode.Version, Operator(), CanonicalId.NewEventId(), CancellationToken.None);

        var error = await Assert.ThrowsAsync<ProductionError>(() =>
            service.ActivateAfterProductionAsync(PersonId, CardClass, 1, 1, "idem-1", Operator(), CanonicalId.NewEventId(), CancellationToken.None));

        Assert.Equal("another_card_already_active", error.Code); // this story never re-implements the invariant - BIO-CRD-005's own gateway still enforces it
    }
}
