// Developer tests for BIO-RPT-002. SYNTHETIC fixtures only.
using Biolog.Features.ReportBuilder.Audit;
using Biolog.Features.ReportBuilder.Domain;
using Xunit;

namespace Biolog.Features.ReportBuilder.Tests;

public sealed class ReportServiceTests
{
    private const string TenantA = "ten_018f2e5a-1234-7abc-89ab-0123456789ab";
    private const string TenantB = "ten_018f2e5a-1234-7abc-89ab-0123456789ac";
    private static readonly AuditActor Actor = new(ActorType.User, "operator-1");
    private static readonly ReportDefinition CardStatusByCount = new() { Dataset = ReportDataset.CardProduction, Dimension = ReportDimension.Status, Measure = ReportMeasure.Count };

    private static ReportService BuildService(FakeCurrentTenantProvider tenantProvider, FakeCardProductionGateway cardGateway, InMemoryReportAuditSink auditSink, out IReportJobRepository jobRepository)
    {
        jobRepository = new InMemoryReportJobRepository();
        var generationService = new ReportGenerationService(cardGateway, new FakeAttendanceGateway());
        var runner = new ReportJobRunner(jobRepository, generationService, auditSink);
        return new ReportService(tenantProvider, jobRepository, runner, auditSink);
    }

    [Fact]
    public void AC01_GetSchema_returns_the_full_whitelist_for_both_datasets()
    {
        var service = BuildService(new FakeCurrentTenantProvider(TenantA), new FakeCardProductionGateway(), new InMemoryReportAuditSink(), out _);

        var schema = service.GetSchema();

        Assert.True(schema.ContainsKey(ReportDataset.CardProduction));
        Assert.True(schema.ContainsKey(ReportDataset.Attendance));
    }

    [Fact]
    public async Task AC03_a_job_created_under_one_tenant_cannot_be_read_under_a_different_tenant_context()
    {
        var tenantProvider = new FakeCurrentTenantProvider(TenantA);
        var cardGateway = new FakeCardProductionGateway();
        cardGateway.Seed(TenantA, new CardProductionRecord("passed", "connected", "staff"));
        var service = BuildService(tenantProvider, cardGateway, new InMemoryReportAuditSink(), out _);

        var job = await service.CreateReportAsync(CardStatusByCount, Actor, "evt_018f2e5a-1234-7abc-89ab-0123456789aa", TestContext.Current.CancellationToken);

        tenantProvider.TenantId = TenantB;
        var error = Assert.Throws<ReportError>(() => service.GetJob(job.JobId));
        Assert.Equal("report_job_not_found", error.Code);
        Assert.Equal(404, error.Status);
    }

    [Fact]
    public async Task AC02_AC05_exporting_a_completed_job_as_csv_succeeds_and_is_audited()
    {
        var tenantProvider = new FakeCurrentTenantProvider(TenantA);
        var cardGateway = new FakeCardProductionGateway();
        cardGateway.Seed(TenantA, new CardProductionRecord("passed", "connected", "staff"));
        var auditSink = new InMemoryReportAuditSink();
        var service = BuildService(tenantProvider, cardGateway, auditSink, out var jobRepository);

        var job = await service.CreateReportAsync(CardStatusByCount, Actor, "evt_018f2e5a-1234-7abc-89ab-0123456789ab", TestContext.Current.CancellationToken);
        await WaitForTerminalAsync(jobRepository, TenantA, job.JobId);

        var export = await service.ExportAsync(job.JobId, ReportExportFormat.Csv, Actor, "evt_018f2e5a-1234-7abc-89ab-0123456789ac", TestContext.Current.CancellationToken);

        Assert.Equal("text/csv", export.ContentType);
        Assert.Contains(auditSink.EventsForTenant(TenantA), e => e.Action == "report.export" && e.Outcome == AuditOutcome.Allowed);
    }

    [Fact]
    public async Task AC02_AC05_exporting_as_excel_is_refused_and_the_refusal_is_audited_as_Denied()
    {
        var tenantProvider = new FakeCurrentTenantProvider(TenantA);
        var cardGateway = new FakeCardProductionGateway();
        cardGateway.Seed(TenantA, new CardProductionRecord("passed", "connected", "staff"));
        var auditSink = new InMemoryReportAuditSink();
        var service = BuildService(tenantProvider, cardGateway, auditSink, out var jobRepository);

        var job = await service.CreateReportAsync(CardStatusByCount, Actor, "evt_018f2e5a-1234-7abc-89ab-0123456789ad", TestContext.Current.CancellationToken);
        await WaitForTerminalAsync(jobRepository, TenantA, job.JobId);

        await Assert.ThrowsAsync<ReportError>(() =>
            service.ExportAsync(job.JobId, ReportExportFormat.Excel, Actor, "evt_018f2e5a-1234-7abc-89ab-0123456789ae", TestContext.Current.CancellationToken));

        Assert.Contains(auditSink.EventsForTenant(TenantA), e => e.Action == "report.export" && e.Outcome == AuditOutcome.Denied);
    }

    [Fact]
    public async Task exporting_a_still_Queued_or_Running_job_is_refused_not_given_a_partial_result()
    {
        var tenantProvider = new FakeCurrentTenantProvider(TenantA);
        var gatedGateway = new GatedCardProductionGateway();
        gatedGateway.Seed(new CardProductionRecord("passed", "connected", "staff"));
        var generationService = new ReportGenerationService(gatedGateway, new FakeAttendanceGateway());
        var jobRepository = new InMemoryReportJobRepository();
        var runner = new ReportJobRunner(jobRepository, generationService, new InMemoryReportAuditSink());
        var service = new ReportService(tenantProvider, jobRepository, runner, new InMemoryReportAuditSink());

        var job = await service.CreateReportAsync(CardStatusByCount, Actor, "evt_018f2e5a-1234-7abc-89ab-0123456789af", TestContext.Current.CancellationToken);

        var error = await Assert.ThrowsAsync<ReportError>(() =>
            service.ExportAsync(job.JobId, ReportExportFormat.Csv, Actor, "evt_018f2e5a-1234-7abc-89ab-0123456789b0", TestContext.Current.CancellationToken));
        Assert.Equal("report_job_not_completed", error.Code);

        gatedGateway.Release();
    }

    private static async Task WaitForTerminalAsync(IReportJobRepository repository, string tenantId, string jobId)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!cts.IsCancellationRequested)
        {
            var job = repository.Get(tenantId, jobId);
            if (job is not null && job.Status.IsTerminal())
            {
                return;
            }

            await Task.Delay(5, CancellationToken.None);
        }

        throw new TimeoutException($"report job '{jobId}' did not reach a terminal state within the test timeout");
    }
}
