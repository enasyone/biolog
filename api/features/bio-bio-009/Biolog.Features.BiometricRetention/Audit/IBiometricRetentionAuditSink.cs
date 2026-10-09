namespace Biolog.Features.BiometricRetention.Audit;

/// <summary>Append-only store for <see cref="BiometricRetentionAuditEvent"/>s (AC02). Mandatory,
/// not optional: <see cref="Service.BiometricDeletionService"/> and <see cref="Service.RetentionPolicyService"/>
/// both await this before treating a mutation as complete (ADR-0003 Decision 7).
/// <see cref="ExistsAsync"/> resolves an ambiguous <see cref="WriteAsync"/> throw - same shape as
/// BIO-BIO-002's own <c>IFingerprintEnrolmentAuditSink</c>.
///
/// <b>AC04 - why audit metadata can never be deleted by this feature.</b> This interface has no
/// delete/remove method of any kind, and nothing in this project references anything that does.
/// A reviewer can verify "deletion propagation never touches audit metadata" by inspecting this
/// one file, the same structural (not merely behavioural) proof BIO-BIO-008-AC02 already
/// established for its own distinctness guarantee.</summary>
public interface IBiometricRetentionAuditSink
{
    Task WriteAsync(BiometricRetentionAuditEvent auditEvent, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default);
}
