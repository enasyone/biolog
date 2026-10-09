using Biolog.Features.ReportBuilder.Audit;
using Biolog.Features.ReportBuilder.Domain;
using Biolog.Features.ReportBuilder.Identifiers;
using Biolog.Features.ReportBuilder.Tenancy;

namespace Biolog.Features.ReportBuilder;

/// <summary>The application-facing surface this feature's endpoints call, the same role BIO-CRD-007's own <c>ProductionService</c>/BIO-ATT-001's own <c>AttendanceEventService</c> play for their own features. Resolves the current tenant itself (AC03) rather than trusting a caller-supplied one.</summary>
internal sealed class ReportService(
    ICurrentTenantProvider tenantProvider,
    IReportJobRepository jobRepository,
    ReportJobRunner jobRunner,
    IReportAuditSink auditSink)
{
    /// <summary>AC01: exposes the whitelist itself so a caller (and a future picker UI) can see exactly which datasets/dimensions/measures are governed - never inferred from a successful/failed report attempt.</summary>
    public IReadOnlyDictionary<ReportDataset, ReportDatasetSchema> GetSchema() => ReportSchemaRegistry.Datasets;

    public async Task<ReportJob> CreateReportAsync(ReportDefinition definition, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        return await jobRunner.CreateAndStartAsync(tenantId, definition, actor, correlationId, cancellationToken);
    }

    public ReportJob GetJob(string jobId)
    {
        var tenantId = RequireTenant();
        return jobRepository.Get(tenantId, jobId) ?? throw ReportError.JobNotFound(jobId);
    }

    public ReportJob CancelJob(string jobId) => jobRunner.Cancel(RequireTenant(), jobId);

    public async Task<ReportExport> ExportAsync(string jobId, ReportExportFormat format, AuditActor actor, string correlationId, CancellationToken cancellationToken = default)
    {
        var job = GetJob(jobId);
        if (job.Status != ReportJobStatus.Completed || job.Result is null)
        {
            throw ReportError.JobNotCompleted(jobId, job.Status);
        }

        var exporter = ReportExporterRegistry.Exporters[format];
        try
        {
            var export = exporter.Export(job.Result, $"report-{job.Definition.Dataset.ToWireString()}-{jobId}");
            await WriteExportAuditAsync(job.TenantId, actor, jobId, format, AuditOutcome.Allowed, "report_exported", correlationId, cancellationToken);
            return export;
        }
        catch (ReportError)
        {
            await WriteExportAuditAsync(job.TenantId, actor, jobId, format, AuditOutcome.Denied, "report_export_format_not_implemented", correlationId, cancellationToken);
            throw;
        }
    }

    private string RequireTenant() => tenantProvider.TenantId ?? throw ReportError.TenantContextMissing();

    private async Task WriteExportAuditAsync(string tenantId, AuditActor actor, string jobId, ReportExportFormat format, AuditOutcome outcome, string reasonCode, string correlationId, CancellationToken cancellationToken)
    {
        var now = UtcInstant.Now();
        await auditSink.WriteAsync(new ReportAuditEvent
        {
            EventId = CanonicalId.NewEventId(),
            TenantId = tenantId,
            Actor = actor,
            Action = "report.export",
            Target = new AuditTarget("report_job", jobId),
            Outcome = outcome,
            ReasonCode = reasonCode,
            CorrelationId = correlationId,
            OccurredAt = now,
            ReceivedAt = now,
            Classification = "internal",
            Detail = new Dictionary<string, object?> { ["format"] = format.ToString().ToLowerInvariant() },
        }, cancellationToken);
    }
}
