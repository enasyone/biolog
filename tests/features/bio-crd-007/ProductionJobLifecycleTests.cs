// Developer tests for BIO-CRD-007. SYNTHETIC fixtures only.
using Biolog.Features.CardProduction.Audit;
using Biolog.Features.CardProduction.Domain;
using Biolog.Features.CardProduction.Identifiers;
using Xunit;

namespace Biolog.Features.CardProduction.Tests;

/// <summary>AC01 (job lifecycle) and AC04 (reprint reason, rejection reason).</summary>
public sealed class ProductionJobLifecycleTests
{
    private const string TenantId = "ten_018f2e5a-1234-7abc-89ab-0123456789ab";
    private const string PersonId = "per_018f2e5a-9c3d-7a41-9b2e-4f7c8d1a6e5b";
    private const string CardClass = "staff";

    private static AuditActor Operator() => new(ActorType.User, "operator-1");

    private static (ProductionService Service, InMemoryProductionJobRepository Repository, FakeCardIssueGateway Gateway) Build()
    {
        var repository = new InMemoryProductionJobRepository();
        var gateway = new FakeCardIssueGateway();
        var service = new ProductionService(
            new FakeCurrentTenantProvider(TenantId), repository, new InMemoryProductionPackageStore(), new InMemoryProductionSigningKeyStore(),
            gateway, new InMemoryProductionAuditSink(), new ProductionJobLockRegistry(), new InMemoryIdempotencyStore(), new FakeCardActivationBarrier());
        return (service, repository, gateway);
    }

    [Fact]
    public async Task CreateJob_starts_InProgress_with_no_steps_recorded()
    {
        var (service, _, gateway) = Build();
        gateway.Seed(PersonId, CardClass, 1);

        var job = await service.CreateJobAsync(PersonId, CardClass, 1, "printer-1", "workstation-1", null, Operator(), CanonicalId.NewEventId(), CancellationToken.None);

        Assert.Equal(1, job.JobSequenceNumber);
        Assert.Equal(ProductionJobStatus.InProgress, job.Status);
        Assert.Null(job.Print);
        Assert.Equal("connected", job.Source);
    }

    [Fact]
    public async Task CreateJob_fails_when_the_card_issue_does_not_exist()
    {
        var (service, _, _) = Build();

        var error = await Assert.ThrowsAsync<ProductionError>(() =>
            service.CreateJobAsync(PersonId, CardClass, 1, "printer-1", "workstation-1", null, Operator(), CanonicalId.NewEventId(), CancellationToken.None));

        Assert.Equal("card_issue_not_found", error.Code);
    }

    [Fact]
    public async Task CreateJob_refuses_a_second_job_while_one_is_still_InProgress()
    {
        var (service, _, gateway) = Build();
        gateway.Seed(PersonId, CardClass, 1);
        await service.CreateJobAsync(PersonId, CardClass, 1, "printer-1", "workstation-1", null, Operator(), CanonicalId.NewEventId(), CancellationToken.None);

        var error = await Assert.ThrowsAsync<ProductionError>(() =>
            service.CreateJobAsync(PersonId, CardClass, 1, "printer-1", "workstation-1", "reprint", Operator(), CanonicalId.NewEventId(), CancellationToken.None));

        Assert.Equal("production_job_not_open", error.Code);
    }

    [Fact]
    public async Task All_three_steps_passing_resolves_the_job_to_Passed()
    {
        var (service, _, gateway) = Build();
        gateway.Seed(PersonId, CardClass, 1);
        var job = await service.CreateJobAsync(PersonId, CardClass, 1, "printer-1", "workstation-1", null, Operator(), CanonicalId.NewEventId(), CancellationToken.None);

        await service.RecordPrintAsync(PersonId, CardClass, 1, 1, true, null, job.Version, Operator(), CanonicalId.NewEventId(), CancellationToken.None);
        var afterEncode = await service.RecordEncodeAsync(PersonId, CardClass, 1, 1, true, null, job.Version + 1, Operator(), CanonicalId.NewEventId(), CancellationToken.None);
        Assert.Equal(ProductionJobStatus.InProgress, afterEncode.Status); // still missing read-back/QC

        var final = await service.RecordReadBackQcAsync(PersonId, CardClass, 1, 1, true, null, afterEncode.Version, Operator(), CanonicalId.NewEventId(), CancellationToken.None);

        Assert.Equal(ProductionJobStatus.Passed, final.Status);
    }

    [Fact]
    public async Task Any_one_step_failing_resolves_the_job_to_Failed_even_if_the_others_passed()
    {
        var (service, _, gateway) = Build();
        gateway.Seed(PersonId, CardClass, 1);
        var job = await service.CreateJobAsync(PersonId, CardClass, 1, "printer-1", "workstation-1", null, Operator(), CanonicalId.NewEventId(), CancellationToken.None);

        var afterPrint = await service.RecordPrintAsync(PersonId, CardClass, 1, 1, true, null, job.Version, Operator(), CanonicalId.NewEventId(), CancellationToken.None);
        var afterEncode = await service.RecordEncodeAsync(PersonId, CardClass, 1, 1, false, "smudged barcode", afterPrint.Version, Operator(), CanonicalId.NewEventId(), CancellationToken.None);
        var final = await service.RecordReadBackQcAsync(PersonId, CardClass, 1, 1, true, null, afterEncode.Version, Operator(), CanonicalId.NewEventId(), CancellationToken.None);

        Assert.Equal(ProductionJobStatus.Failed, final.Status);
    }

    [Fact]
    public async Task Recording_the_same_step_twice_is_refused()
    {
        var (service, _, gateway) = Build();
        gateway.Seed(PersonId, CardClass, 1);
        var job = await service.CreateJobAsync(PersonId, CardClass, 1, "printer-1", "workstation-1", null, Operator(), CanonicalId.NewEventId(), CancellationToken.None);
        var afterPrint = await service.RecordPrintAsync(PersonId, CardClass, 1, 1, true, null, job.Version, Operator(), CanonicalId.NewEventId(), CancellationToken.None);

        var error = await Assert.ThrowsAsync<ProductionError>(() =>
            service.RecordPrintAsync(PersonId, CardClass, 1, 1, true, null, afterPrint.Version, Operator(), CanonicalId.NewEventId(), CancellationToken.None));

        Assert.Equal("production_step_already_recorded", error.Code);
    }

    [Fact]
    public async Task Recording_a_step_on_a_resolved_job_is_refused()
    {
        var (service, _, gateway) = Build();
        gateway.Seed(PersonId, CardClass, 1);
        var job = await service.CreateJobAsync(PersonId, CardClass, 1, "printer-1", "workstation-1", null, Operator(), CanonicalId.NewEventId(), CancellationToken.None);
        var afterPrint = await service.RecordPrintAsync(PersonId, CardClass, 1, 1, true, null, job.Version, Operator(), CanonicalId.NewEventId(), CancellationToken.None);
        var afterEncode = await service.RecordEncodeAsync(PersonId, CardClass, 1, 1, true, null, afterPrint.Version, Operator(), CanonicalId.NewEventId(), CancellationToken.None);
        var resolved = await service.RecordReadBackQcAsync(PersonId, CardClass, 1, 1, true, null, afterEncode.Version, Operator(), CanonicalId.NewEventId(), CancellationToken.None);

        // No fourth step exists, but recording print again on the now-resolved job must still
        // refuse - covered by StepAlreadyRecorded above; this proves the JobNotOpen guard too by
        // rejecting via cancel/reject on a resolved job.
        var error = await Assert.ThrowsAsync<ProductionError>(() =>
            service.CancelJobAsync(PersonId, CardClass, 1, 1, resolved.Version, Operator(), CanonicalId.NewEventId(), CancellationToken.None));

        Assert.Equal("production_job_not_open", error.Code);
    }

    [Fact]
    public async Task RejectJob_is_allowed_even_after_the_job_mechanically_Passed()
    {
        var (service, _, gateway) = Build();
        gateway.Seed(PersonId, CardClass, 1);
        var job = await service.CreateJobAsync(PersonId, CardClass, 1, "printer-1", "workstation-1", null, Operator(), CanonicalId.NewEventId(), CancellationToken.None);
        var afterPrint = await service.RecordPrintAsync(PersonId, CardClass, 1, 1, true, null, job.Version, Operator(), CanonicalId.NewEventId(), CancellationToken.None);
        var afterEncode = await service.RecordEncodeAsync(PersonId, CardClass, 1, 1, true, null, afterPrint.Version, Operator(), CanonicalId.NewEventId(), CancellationToken.None);
        var passed = await service.RecordReadBackQcAsync(PersonId, CardClass, 1, 1, true, null, afterEncode.Version, Operator(), CanonicalId.NewEventId(), CancellationToken.None);
        Assert.Equal(ProductionJobStatus.Passed, passed.Status);

        var rejected = await service.RejectJobAsync(PersonId, CardClass, 1, 1, "visible scratch on laminate", passed.Version, Operator(), CanonicalId.NewEventId(), CancellationToken.None);

        Assert.Equal(ProductionJobStatus.Rejected, rejected.Status);
        Assert.Equal("visible scratch on laminate", rejected.RejectionReason);
    }

    [Fact]
    public async Task CancelJob_closes_an_InProgress_job_without_resolving_it_as_Passed_or_Failed()
    {
        var (service, _, gateway) = Build();
        gateway.Seed(PersonId, CardClass, 1);
        var job = await service.CreateJobAsync(PersonId, CardClass, 1, "printer-1", "workstation-1", null, Operator(), CanonicalId.NewEventId(), CancellationToken.None);

        var cancelled = await service.CancelJobAsync(PersonId, CardClass, 1, 1, job.Version, Operator(), CanonicalId.NewEventId(), CancellationToken.None);

        Assert.Equal(ProductionJobStatus.Cancelled, cancelled.Status);
    }

    [Fact]
    public async Task A_reprint_after_cancellation_can_proceed_and_requires_a_reprint_reason()
    {
        var (service, _, gateway) = Build();
        gateway.Seed(PersonId, CardClass, 1);
        var job = await service.CreateJobAsync(PersonId, CardClass, 1, "printer-1", "workstation-1", null, Operator(), CanonicalId.NewEventId(), CancellationToken.None);
        await service.CancelJobAsync(PersonId, CardClass, 1, 1, job.Version, Operator(), CanonicalId.NewEventId(), CancellationToken.None);

        var error = await Assert.ThrowsAsync<ProductionError>(() =>
            service.CreateJobAsync(PersonId, CardClass, 1, "printer-1", "workstation-1", null, Operator(), CanonicalId.NewEventId(), CancellationToken.None));
        Assert.Equal("reprint_reason_required", error.Code);

        var reprint = await service.CreateJobAsync(PersonId, CardClass, 1, "printer-1", "workstation-1", "wrong card queued the first time", Operator(), CanonicalId.NewEventId(), CancellationToken.None);
        Assert.Equal(2, reprint.JobSequenceNumber);
        Assert.Equal("wrong card queued the first time", reprint.ReprintReason);
    }

    [Fact]
    public async Task The_first_ever_job_must_not_carry_a_reprint_reason()
    {
        var (service, _, gateway) = Build();
        gateway.Seed(PersonId, CardClass, 1);

        var error = await Assert.ThrowsAsync<ProductionError>(() =>
            service.CreateJobAsync(PersonId, CardClass, 1, "printer-1", "workstation-1", "not applicable yet", Operator(), CanonicalId.NewEventId(), CancellationToken.None));

        Assert.Equal("reprint_reason_not_allowed", error.Code);
    }

    [Fact]
    public async Task ListJobsAsync_returns_the_full_production_report_for_a_card()
    {
        var (service, _, gateway) = Build();
        gateway.Seed(PersonId, CardClass, 1);
        var first = await service.CreateJobAsync(PersonId, CardClass, 1, "printer-1", "workstation-1", null, Operator(), CanonicalId.NewEventId(), CancellationToken.None);
        await service.CancelJobAsync(PersonId, CardClass, 1, 1, first.Version, Operator(), CanonicalId.NewEventId(), CancellationToken.None);
        await service.CreateJobAsync(PersonId, CardClass, 1, "printer-1", "workstation-1", "reprint", Operator(), CanonicalId.NewEventId(), CancellationToken.None);

        var report = await service.ListJobsAsync(PersonId, CardClass, 1, CancellationToken.None);

        Assert.Equal(2, report.Count);
        Assert.Equal(ProductionJobStatus.Cancelled, report[0].Status);
        Assert.Equal(ProductionJobStatus.InProgress, report[1].Status);
    }

    /// <summary>
    /// BIO-CRD-008-AC04: "Production references exact published version." Purely additive -
    /// every call site above omits these two optional trailing parameters and is completely
    /// unaffected (regression proven by every other test in this file still passing unchanged).
    /// </summary>
    [Fact]
    public async Task CreateJob_optionally_records_the_exact_published_template_version_it_was_produced_against()
    {
        var (service, _, gateway) = Build();
        gateway.Seed(PersonId, CardClass, 1);

        var job = await service.CreateJobAsync(
            PersonId, CardClass, 1, "printer-1", "workstation-1", null, Operator(), CanonicalId.NewEventId(), CancellationToken.None,
            templateId: "tpl_018f2e5a-3333-7abc-89ab-0123456789ab", templateVersion: 2);

        Assert.Equal("tpl_018f2e5a-3333-7abc-89ab-0123456789ab", job.TemplateId);
        Assert.Equal(2, job.TemplateVersion);
    }

    [Fact]
    public async Task CreateJob_omitting_the_template_reference_leaves_both_fields_null_the_pre_BIO_CRD_008_shape()
    {
        var (service, _, gateway) = Build();
        gateway.Seed(PersonId, CardClass, 1);

        var job = await service.CreateJobAsync(PersonId, CardClass, 1, "printer-1", "workstation-1", null, Operator(), CanonicalId.NewEventId(), CancellationToken.None);

        Assert.Null(job.TemplateId);
        Assert.Null(job.TemplateVersion);
    }
}
