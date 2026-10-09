namespace Biolog.Features.TenantFeatureConfig.Audit;

/// <summary>
/// Durable append-only store for <see cref="TenantFeatureConfigurationAuditEvent"/>s.
/// Mandatory, not optional (mirrors ADR-0002 §5's "the audit sink is a hard dependency", the
/// same discipline BIO-TEN-001 established): <c>TenantFeatureConfigurationService</c> awaits
/// this before treating a configuration change as complete, and a failure here surfaces to
/// the caller as <c>audit_unavailable</c> rather than a silent, unaudited success.
///
/// <see cref="ExistsAsync"/> exists so a caller can resolve a <see cref="WriteAsync"/> throw by
/// checking ground truth instead of assuming failure - copied from BIO-TEN-001-QA-01 round 4's
/// finding (<c>ITenantAuditSink</c>'s own remarks), the identical ambiguity applies to any
/// append-only sink in this codebase.
/// </summary>
public interface ITenantFeatureConfigurationAuditSink
{
    Task WriteAsync(TenantFeatureConfigurationAuditEvent auditEvent, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default);
}
