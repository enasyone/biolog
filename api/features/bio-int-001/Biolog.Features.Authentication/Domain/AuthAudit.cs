namespace Biolog.Features.Authentication;

/// <summary>
/// BIO-INT-001-AC04: authentication failure and account-link events are audited. Field shape
/// follows ADR-0003's own audit-event fields (event id, tenant, actor/subject, action,
/// outcome-carrying reason, correlation, timestamps) - this project's own copy, the same
/// "mirror the ADR shape, don't share the C# type" pattern <c>DomainEventEnvelope</c> already
/// documents in BIO-RPT-001, since a feature project cannot reference <c>Biolog.Api.Audit</c>.
/// Never carries a raw password/credential value - only <see cref="ProviderId"/>,
/// <see cref="SubjectId"/> (the provider's own subject identifier, not a secret) and a closed
/// <see cref="FailureReason"/> enum, never free-text exception detail.
/// </summary>
public sealed record AuthAuditEvent(
    string EventId,
    int SchemaVersion,
    string TenantId,
    AuthAuditAction Action,
    string ProviderId,
    string? SubjectId,
    AuthenticationFailureReason? FailureReason,
    string CorrelationId,
    DateTimeOffset OccurredAt);

/// <summary>
/// Write-or-throw with an existence-recheck retry, matching BIO-IDN-006's own
/// <c>IRbacAuditSink</c> discipline (never fire-and-forget an audit write): on a write
/// exception, check whether the event was durably recorded anyway before raising
/// <see cref="AuditUnavailableException"/> - a transient failure that actually landed must
/// not be reported as lost.
/// </summary>
public interface IAuthAuditSink
{
    Task WriteAsync(AuthAuditEvent auditEvent, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken);

    Task<IReadOnlyList<AuthAuditEvent>> ListForTenantAsync(string tenantId, CancellationToken cancellationToken);
}

public sealed class AuditUnavailableException() : Exception("authentication audit sink unavailable");

public sealed class InMemoryAuthAuditSink : IAuthAuditSink
{
    private readonly List<AuthAuditEvent> _events = [];
    private readonly Lock _gate = new();

    public Task WriteAsync(AuthAuditEvent auditEvent, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _events.Add(auditEvent);
        }

        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(_events.Any(e => e.EventId == eventId));
        }
    }

    public Task<IReadOnlyList<AuthAuditEvent>> ListForTenantAsync(string tenantId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            IReadOnlyList<AuthAuditEvent> result = _events.Where(e => e.TenantId == tenantId).ToList();
            return Task.FromResult(result);
        }
    }
}
