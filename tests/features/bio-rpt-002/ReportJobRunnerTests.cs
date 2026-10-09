// Developer tests for BIO-RPT-002. SYNTHETIC fixtures only.
using Biolog.Features.ReportBuilder.Audit;
using Biolog.Features.ReportBuilder.Domain;
using Xunit;

namespace Biolog.Features.ReportBuilder.Tests;

public sealed class ReportJobRunnerTests
{
    private const string TenantA = "ten_018f2e5a-1234-7abc-89ab-0123456789ab";
    private static readonly AuditActor Actor = new(ActorType.User, "operator-1");
    private static readonly ReportDefinition CardStatusByCount = new() { Dataset = ReportDataset.CardProduction, Dimension = ReportDimension.Status, Measure = ReportMeasure.Count };

    [Fact]
    public async Task AC04_a_job_starts_Queued_and_reaches_Completed_with_a_real_result()
    {
        var jobRepository = new InMemoryReportJobRepositoryForTests();
        var cardGateway = new FakeCardProductionGateway();
        cardGateway.Seed(TenantA, new CardProductionRecord("passed", "connected", "staff"));
        var auditSink = new InMemoryReportAuditSink();
        var runner = new ReportJobRunner(jobRepository, new ReportGenerationService(cardGateway, new FakeAttendanceGateway()), auditSink);

        var job = await runner.CreateAndStartAsync(TenantA, CardStatusByCount, Actor, "evt_018f2e5a-1234-7abc-89ab-0123456789aa", TestContext.Current.CancellationToken);
        Assert.Equal(ReportJobStatus.Queued, job.Status);

        var completed = await WaitForTerminalAsync(jobRepository, TenantA, job.JobId);

        Assert.Equal(ReportJobStatus.Completed, completed.Status);
        Assert.NotNull(completed.Result);
        Assert.Equal(1, completed.Result!.SourceRecordCount);

        var events = auditSink.EventsForTenant(TenantA);
        Assert.Contains(events, e => e.Action == "report.generate" && e.ReasonCode == "report_job_queued");
        Assert.Contains(events, e => e.Action == "report.generate" && e.ReasonCode == "report_job_completed" && e.Outcome == AuditOutcome.Allowed);
    }

    [Fact]
    public async Task AC04_cancelling_a_running_job_deterministically_stops_it_at_Cancelled()
    {
        var jobRepository = new InMemoryReportJobRepositoryForTests();
        var gatedGateway = new GatedCardProductionGateway();
        gatedGateway.Seed(new CardProductionRecord("passed", "connected", "staff"));
        var auditSink = new InMemoryReportAuditSink();
        var runner = new ReportJobRunner(jobRepository, new ReportGenerationService(gatedGateway, new FakeAttendanceGateway()), auditSink);

        var job = await runner.CreateAndStartAsync(TenantA, CardStatusByCount, Actor, "evt_018f2e5a-1234-7abc-89ab-0123456789ab", TestContext.Current.CancellationToken);

        // Wait until the background Task has actually reached Running (past the gate) before
        // cancelling - otherwise cancelling while still Queued would prove nothing about
        // mid-flight cancellation specifically.
        await WaitForStatusAsync(jobRepository, TenantA, job.JobId, ReportJobStatus.Running);

        runner.Cancel(TenantA, job.JobId);
        // Deliberately never call gatedGateway.Release() - if cancellation did not propagate,
        // this test would hang until WaitForTerminalAsync's own timeout fails it loudly rather
        // than a silent false pass.
        var final = await WaitForTerminalAsync(jobRepository, TenantA, job.JobId);

        Assert.Equal(ReportJobStatus.Cancelled, final.Status);
        Assert.Contains(auditSink.EventsForTenant(TenantA), e => e.ReasonCode == "report_job_cancelled");
    }

    [Fact]
    public async Task AC04_a_job_that_has_already_reached_a_terminal_state_cannot_be_cancelled_again()
    {
        var jobRepository = new InMemoryReportJobRepositoryForTests();
        var cardGateway = new FakeCardProductionGateway();
        cardGateway.Seed(TenantA, new CardProductionRecord("passed", "connected", "staff"));
        var runner = new ReportJobRunner(jobRepository, new ReportGenerationService(cardGateway, new FakeAttendanceGateway()), new InMemoryReportAuditSink());

        var job = await runner.CreateAndStartAsync(TenantA, CardStatusByCount, Actor, "evt_018f2e5a-1234-7abc-89ab-0123456789ac", TestContext.Current.CancellationToken);
        await WaitForTerminalAsync(jobRepository, TenantA, job.JobId);

        var error = Assert.Throws<ReportError>(() => runner.Cancel(TenantA, job.JobId));
        Assert.Equal("report_job_not_cancellable", error.Code);
    }

    [Fact]
    public async Task a_job_whose_generation_throws_ends_Failed_with_an_error_code_not_left_Running_forever()
    {
        var jobRepository = new InMemoryReportJobRepositoryForTests();
        var throwingGateway = new ThrowingCardProductionGateway();
        var auditSink = new InMemoryReportAuditSink();
        var runner = new ReportJobRunner(jobRepository, new ReportGenerationService(throwingGateway, new FakeAttendanceGateway()), auditSink);

        var job = await runner.CreateAndStartAsync(TenantA, CardStatusByCount, Actor, "evt_018f2e5a-1234-7abc-89ab-0123456789ad", TestContext.Current.CancellationToken);
        var final = await WaitForTerminalAsync(jobRepository, TenantA, job.JobId);

        Assert.Equal(ReportJobStatus.Failed, final.Status);
        Assert.NotNull(final.ErrorCode);
        Assert.Contains(auditSink.EventsForTenant(TenantA), e => e.Outcome == AuditOutcome.Error);
    }

    private static async Task<ReportJob> WaitForTerminalAsync(IReportJobRepository repository, string tenantId, string jobId)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!cts.IsCancellationRequested)
        {
            var job = repository.Get(tenantId, jobId);
            if (job is not null && job.Status.IsTerminal())
            {
                return job;
            }

            await Task.Delay(5, CancellationToken.None);
        }

        throw new TimeoutException($"report job '{jobId}' did not reach a terminal state within the test timeout");
    }

    private static async Task WaitForStatusAsync(IReportJobRepository repository, string tenantId, string jobId, ReportJobStatus status)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!cts.IsCancellationRequested)
        {
            var job = repository.Get(tenantId, jobId);
            if (job is not null && job.Status == status)
            {
                return;
            }

            await Task.Delay(5, CancellationToken.None);
        }

        throw new TimeoutException($"report job '{jobId}' did not reach status '{status}' within the test timeout");
    }

    private sealed class InMemoryReportJobRepositoryForTests : IReportJobRepository
    {
        private readonly Dictionary<string, ReportJob> _byKey = new(StringComparer.Ordinal);
        private readonly Lock _lock = new();

        public ReportJob Create(ReportJob job)
        {
            lock (_lock)
            {
                _byKey[Key(job.TenantId, job.JobId)] = job;
                return job;
            }
        }

        public ReportJob? Get(string tenantId, string jobId)
        {
            lock (_lock)
            {
                return _byKey.TryGetValue(Key(tenantId, jobId), out var job) ? job : null;
            }
        }

        public ReportJob Replace(ReportJob expected, ReportJob updated)
        {
            lock (_lock)
            {
                var key = Key(expected.TenantId, expected.JobId);
                if (!_byKey.TryGetValue(key, out var current) || current.Version != expected.Version)
                {
                    throw new ReportError("concurrency conflict", "report_job_concurrency_conflict", 409);
                }

                _byKey[key] = updated;
                return updated;
            }
        }

        private static string Key(string tenantId, string jobId) => $"{tenantId}\u0000{jobId}";
    }

    private sealed class ThrowingCardProductionGateway : ICardProductionGateway
    {
        public Task<IReadOnlyList<CardProductionRecord>> ListAsync(string tenantId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("synthetic gateway failure for test purposes");
    }
}
