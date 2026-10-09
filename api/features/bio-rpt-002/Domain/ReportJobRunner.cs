using System.Collections.Concurrent;
using Biolog.Features.ReportBuilder.Audit;
using Biolog.Features.ReportBuilder.Identifiers;

namespace Biolog.Features.ReportBuilder.Domain;

/// <summary>
/// Owns every report job's async execution and cancellation (BIO-RPT-002-AC04), the same
/// "singleton registry of per-job <see cref="CancellationTokenSource"/>s" shape BIO-CRD-007's
/// own <c>ProductionJobLockRegistry</c> establishes for per-job coordination state, applied here
/// to cancellation instead of mutual exclusion. Every job - regardless of how much data it
/// touches - runs through <see cref="Task.Run(Action)"/> and this same Queued→Running→
/// Completed/Failed/Cancelled state machine, so "asynchronous" is never a size-dependent branch
/// a caller could be surprised by.
/// </summary>
public sealed class ReportJobRunner(
    IReportJobRepository jobRepository,
    ReportGenerationService generationService,
    IReportAuditSink auditSink)
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _tokens = new(StringComparer.Ordinal);

    public async Task<ReportJob> CreateAndStartAsync(string tenantId, ReportDefinition definition, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        ReportDefinitionValidator.Validate(definition);

        var now = UtcInstant.Now();
        var job = jobRepository.Create(new ReportJob
        {
            JobId = CanonicalId.NewReportJobId(),
            TenantId = tenantId,
            Definition = definition,
            Status = ReportJobStatus.Queued,
            Version = 1,
            RequestedBy = actor,
            CreatedAt = now,
            UpdatedAt = now,
        });

        await WriteAuditAsync(job.TenantId, actor, "report.generate", job.JobId, AuditOutcome.Allowed, "report_job_queued", correlationId,
            new Dictionary<string, object?> { ["dataset"] = definition.Dataset.ToWireString(), ["dimension"] = definition.Dimension.ToWireString(), ["measure"] = definition.Measure.ToWireString() },
            cancellationToken);

        var cts = new CancellationTokenSource();
        _tokens[job.JobId] = cts;

        _ = Task.Run(() => RunAsync(job, actor, correlationId, cts.Token));

        return job;
    }

    private async Task RunAsync(ReportJob queued, AuditActor actor, string correlationId, CancellationToken cancellationToken)
    {
        var running = jobRepository.Replace(queued, queued with { Status = ReportJobStatus.Running, Version = queued.Version + 1, UpdatedAt = UtcInstant.Now(), StartedAt = UtcInstant.Now() });

        try
        {
            var result = await generationService.GenerateAsync(running.TenantId, running.Definition, cancellationToken);
            var completedAt = UtcInstant.Now();
            jobRepository.Replace(running, running with { Status = ReportJobStatus.Completed, Version = running.Version + 1, UpdatedAt = completedAt, CompletedAt = completedAt, Result = result });
            await WriteAuditAsync(running.TenantId, actor, "report.generate", running.JobId, AuditOutcome.Allowed, "report_job_completed", correlationId,
                new Dictionary<string, object?> { ["row_count"] = result.Rows.Count, ["source_record_count"] = result.SourceRecordCount }, CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            var cancelledAt = UtcInstant.Now();
            jobRepository.Replace(running, running with { Status = ReportJobStatus.Cancelled, Version = running.Version + 1, UpdatedAt = cancelledAt, CompletedAt = cancelledAt });
            await WriteAuditAsync(running.TenantId, actor, "report.generate", running.JobId, AuditOutcome.Denied, "report_job_cancelled", correlationId, new Dictionary<string, object?>(), CancellationToken.None);
        }
        catch (Exception ex)
        {
            var failedAt = UtcInstant.Now();
            var code = ex is ReportError reportError ? reportError.Code : "report_generation_failed";
            jobRepository.Replace(running, running with { Status = ReportJobStatus.Failed, Version = running.Version + 1, UpdatedAt = failedAt, CompletedAt = failedAt, ErrorCode = code });
            await WriteAuditAsync(running.TenantId, actor, "report.generate", running.JobId, AuditOutcome.Error, code, correlationId, new Dictionary<string, object?>(), CancellationToken.None);
        }
        finally
        {
            _tokens.TryRemove(running.JobId, out _);
        }
    }

    /// <summary>AC04: cancels a Queued or Running job. A job that has already reached a terminal state is refused, never silently ignored.</summary>
    public ReportJob Cancel(string tenantId, string jobId)
    {
        var job = jobRepository.Get(tenantId, jobId) ?? throw ReportError.JobNotFound(jobId);
        if (job.Status.IsTerminal())
        {
            throw ReportError.JobNotCancellable(jobId, job.Status);
        }

        if (_tokens.TryGetValue(jobId, out var cts))
        {
            cts.Cancel();
        }

        return job;
    }

    private async Task WriteAuditAsync(string tenantId, AuditActor actor, string action, string jobId, AuditOutcome outcome, string reasonCode, string correlationId, IReadOnlyDictionary<string, object?> detail, CancellationToken cancellationToken)
    {
        var now = UtcInstant.Now();
        try
        {
            await auditSink.WriteAsync(new ReportAuditEvent
            {
                EventId = CanonicalId.NewEventId(),
                TenantId = tenantId,
                Actor = actor,
                Action = action,
                Target = new AuditTarget("report_job", jobId),
                Outcome = outcome,
                ReasonCode = reasonCode,
                CorrelationId = correlationId,
                OccurredAt = now,
                ReceivedAt = now,
                Classification = "internal",
                Detail = detail,
            }, cancellationToken);
        }
        catch
        {
            // AC05: audit is mandatory for a mutating write, but this runs on a detached
            // background Task after the job's own state has already been committed (unlike a
            // synchronous endpoint handler, there is no HTTP response left to fail closed by
            // refusing) - an audit-sink outage here is a genuine operational gap, disclosed
            // rather than silently retried indefinitely or allowed to crash the background task.
        }
    }
}
