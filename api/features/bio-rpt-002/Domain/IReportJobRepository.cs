using System.Collections.Concurrent;

namespace Biolog.Features.ReportBuilder.Domain;

/// <summary>Tenant-scoped report-job storage. A single, process-lifetime, cross-tenant dictionary standing in for the per-tenant database ADR-0002 Decision 4.1 describes - the same simplification every in-memory repository in this codebase draws around itself (ADR-0005).</summary>
public interface IReportJobRepository
{
    ReportJob Create(ReportJob job);

    /// <summary>AC03: null both when the job never existed AND when it exists for a different tenant - the caller cannot distinguish "not found" from "not yours" (see <c>Domain/ReportError.JobNotFound</c>'s own 404, never a 403 that would confirm existence).</summary>
    ReportJob? Get(string tenantId, string jobId);

    /// <summary>Compare-and-set replace (ADR-0001 §3.2).</summary>
    ReportJob Replace(ReportJob expected, ReportJob updated);
}

internal sealed class InMemoryReportJobRepository : IReportJobRepository
{
    private readonly ConcurrentDictionary<string, ReportJob> _byKey = new(StringComparer.Ordinal);

    public ReportJob Create(ReportJob job)
    {
        var key = Key(job.TenantId, job.JobId);
        if (!_byKey.TryAdd(key, job))
        {
            throw new ReportError($"report job collision for id '{job.JobId}'", "report_job_collision", 500,
                new Dictionary<string, object?> { ["job_id"] = job.JobId });
        }

        return job;
    }

    public ReportJob? Get(string tenantId, string jobId) =>
        _byKey.TryGetValue(Key(tenantId, jobId), out var job) ? job : null;

    public ReportJob Replace(ReportJob expected, ReportJob updated)
    {
        var key = Key(expected.TenantId, expected.JobId);

        if (!_byKey.TryGetValue(key, out var current))
        {
            throw ReportError.JobNotFound(expected.JobId);
        }

        if (!_byKey.TryUpdate(key, updated, expected))
        {
            var latest = _byKey.TryGetValue(key, out var p) ? p : current;
            throw new ReportError(
                "the report job was modified since the version this request expected", "report_job_concurrency_conflict", 409,
                new Dictionary<string, object?> { ["expected_version"] = expected.Version, ["actual_version"] = latest.Version });
        }

        return updated;
    }

    private static string Key(string tenantId, string jobId) => $"{tenantId}\u0000{jobId}";
}
