namespace Biolog.Features.Integrations.Audit;

/// <summary>ADR-0003 Decision 7's audit-atomicity seam: <see cref="ExistsAsync"/> is what lets a service resolve an ambiguous write-throw instead of assuming failure.</summary>
public interface IImportAuditSink
{
    Task WriteAsync(ImportAuditEvent auditEvent, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default);
}
