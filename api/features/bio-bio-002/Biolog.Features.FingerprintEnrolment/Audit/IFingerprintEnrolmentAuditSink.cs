namespace Biolog.Features.FingerprintEnrolment.Audit;

/// <summary>Append-only store for <see cref="FingerprintEnrolmentAuditEvent"/>s (AC05). Mandatory,
/// not optional: <see cref="Service.FingerprintEnrolmentService"/> awaits this before treating a
/// mutation as complete (ADR-0003 Decision 7). <see cref="ExistsAsync"/> resolves an ambiguous
/// <see cref="WriteAsync"/> throw (a real store can commit and still throw on acknowledgement) -
/// same shape as BIO-IDN-001's <c>IPersonAuditSink</c>.</summary>
public interface IFingerprintEnrolmentAuditSink
{
    Task WriteAsync(FingerprintEnrolmentAuditEvent auditEvent, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default);
}
