using System.Collections.Concurrent;

namespace Biolog.Features.ReportBuilder.Audit;

/// <summary>In-process, append-only audit store for BIO-RPT-002. Process-lifetime only. See <see cref="IReportAuditSink"/>.</summary>
public sealed class InMemoryReportAuditSink : IReportAuditSink
{
    private readonly ConcurrentQueue<ReportAuditEvent> _events = new();

    public Task WriteAsync(ReportAuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        _events.Enqueue(auditEvent);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_events.Any(e => e.EventId == eventId));

    /// <summary>Read-only snapshot, oldest first, tenant-filtered (AC03). Exposed for tests and <see cref="Api.ReportEndpoints"/>'s own audit-read endpoint.</summary>
    public IReadOnlyList<ReportAuditEvent> EventsForTenant(string tenantId) =>
        [.. _events.Where(e => e.TenantId == tenantId)];
}
