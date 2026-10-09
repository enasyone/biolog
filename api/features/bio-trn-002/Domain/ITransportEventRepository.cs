namespace Biolog.Features.TransportEvents.Domain;

/// <summary>Storage for <see cref="TransportEvent"/>. Every read takes <c>tenantId</c> explicitly. <c>internal</c> (BIO-IDN-001-QA-01 discipline) - restricted to this assembly plus <c>Biolog.Api</c> and <c>Biolog.Features.TransportEvents.Tests</c> via <c>InternalsVisibleTo</c>.</summary>
internal interface ITransportEventRepository
{
    Task<TransportEvent> CreateAsync(TransportEvent record, CancellationToken cancellationToken = default);

    Task<TransportEvent?> GetByIdempotencyKeyAsync(string tenantId, string idempotencyKey, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TransportEvent>> ListForPersonOnDateAsync(string tenantId, string personId, string date, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TransportEvent>> ListForTenantAsync(string tenantId, CancellationToken cancellationToken = default);
}
