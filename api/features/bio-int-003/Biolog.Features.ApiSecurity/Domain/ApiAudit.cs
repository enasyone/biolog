namespace Biolog.Features.ApiSecurity;

public enum ApiAuditAction
{
    ServiceAccountCreated,
    SecretRotated,
    ServiceAccountRevoked,
    TokenIssued,
    TokenIssueFailed,
    RateLimitExceeded,
    /// <summary>BIO-INT-003-ZB-03: a structurally-valid, unexpired bearer token was rejected at
    /// AUTHORIZE time because live account state no longer supports it - the account was
    /// revoked/deleted since the token was issued, or the token's scope is no longer (or was
    /// never) among the account's current live grants.</summary>
    AuthorizationDenied,
}

/// <summary>
/// BIO-INT-003-AC03: API-level security audit. Field shape follows ADR-0003's own audit-event
/// fields, this project's own copy (the same "mirror the ADR shape, don't share the C# type"
/// pattern BIO-RPT-001's <c>DomainEventEnvelope</c> and BIO-INT-001's own <c>AuthAuditEvent</c>
/// already document - a feature project cannot reference <c>Biolog.Api.Audit</c> or a sibling
/// feature project's audit type). Never carries a raw client secret or access token - only
/// <see cref="ClientId"/> (a public identifier, not a secret) and a closed
/// <see cref="ApiAuditAction"/> enum.
///
/// <see cref="ActorId"/> (security-review fix, Zainab, ZB-INT003-02) records WHO performed a
/// management mutation (create/rotate/revoke) - the same resolved <c>X-Actor-Id</c>
/// <see cref="Api.ActorProvenanceGate"/> verified before the operation was allowed to proceed.
/// <c>null</c> for the OAuth2 token-issuance/rate-limit events, where <see cref="ClientId"/>
/// itself already identifies the caller and there is no separate human actor to name.
/// </summary>
public sealed record ApiAuditEvent(
    string EventId,
    int SchemaVersion,
    string TenantId,
    ApiAuditAction Action,
    string ClientId,
    string? ActorId,
    string? Detail,
    string CorrelationId,
    DateTimeOffset OccurredAt);

/// <summary>
/// Write-or-throw with an existence-recheck retry, matching BIO-INT-001's own
/// <c>IAuthAuditSink</c> discipline (never fire-and-forget a security-relevant audit write).
/// </summary>
public interface IApiAuditSink
{
    Task WriteAsync(ApiAuditEvent auditEvent, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ApiAuditEvent>> ListForTenantAsync(string tenantId, CancellationToken cancellationToken);
}

public sealed class ApiAuditUnavailableException() : Exception("API audit sink unavailable");

public sealed class InMemoryApiAuditSink : IApiAuditSink
{
    private readonly List<ApiAuditEvent> _events = [];
    private readonly Lock _gate = new();

    public Task WriteAsync(ApiAuditEvent auditEvent, CancellationToken cancellationToken)
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

    public Task<IReadOnlyList<ApiAuditEvent>> ListForTenantAsync(string tenantId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            IReadOnlyList<ApiAuditEvent> result = _events.Where(e => e.TenantId == tenantId).ToList();
            return Task.FromResult(result);
        }
    }
}
