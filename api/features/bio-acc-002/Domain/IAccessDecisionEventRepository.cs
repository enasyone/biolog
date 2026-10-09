namespace Biolog.Features.AccessDecisions.Domain;

/// <summary>Storage for <see cref="AccessDecisionEvent"/>. Every read takes <c>tenantId</c> explicitly. <c>internal</c> (BIO-IDN-001-QA-01 discipline) - restricted to this assembly plus <c>Biolog.Api</c> and <c>Biolog.Features.AccessDecisions.Tests</c> via <c>InternalsVisibleTo</c>.</summary>
internal interface IAccessDecisionEventRepository
{
    Task<AccessDecisionEvent> CreateAsync(AccessDecisionEvent record, CancellationToken cancellationToken = default);

    Task<AccessDecisionEvent?> GetByIdempotencyKeyAsync(string tenantId, string idempotencyKey, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AccessDecisionEvent>> ListForPersonAsync(string tenantId, string personId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AccessDecisionEvent>> ListForTenantAsync(string tenantId, CancellationToken cancellationToken = default);
}
