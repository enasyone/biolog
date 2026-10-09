namespace Biolog.Api.Features.DeviceCertification.Audit;

/// <summary>
/// Where a <see cref="CertificationAuditEvent"/> is recorded (BIO-DEV-002-AC04). Mirrors
/// <see cref="Biolog.Api.Audit.IAuditSink"/> exactly, including <see cref="ExistsAsync"/>'s
/// role in resolving an ambiguous <see cref="RecordAsync"/> throw (ADR-0003 Decision 7,
/// element 4: a real store can durably commit a write and still throw on the
/// acknowledgement) - see <see cref="DeviceCertificationService"/>'s private
/// <c>AuditAsync</c> for how it is used, and
/// <see cref="Features.DeviceRegistry.DeviceService.AuditAsync"/> for the reference
/// implementation this mirrors line-for-line.
/// </summary>
public interface ICertificationAuditSink
{
    Task RecordAsync(CertificationAuditEvent auditEvent, CancellationToken cancellationToken);

    /// <summary>True if an event with this exact <c>event_id</c> is present, false if it
    /// definitely is not. If THIS also throws, that is a genuine, total outage of the
    /// audit store, not merely one ambiguous write.</summary>
    Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken);
}
