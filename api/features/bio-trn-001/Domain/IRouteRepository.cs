namespace Biolog.Features.Transport.Domain;

/// <summary>
/// Storage for <see cref="RouteRecord"/>. Every read/write takes <c>tenantId</c> explicitly and
/// asserts a matching record's own <see cref="RouteRecord.TenantId"/> before returning it.
///
/// <c>internal</c> (BIO-IDN-001-QA-01 discipline) - restricted to this assembly plus
/// <c>Biolog.Api</c> (DI registration) and <c>Biolog.Features.Transport.Tests</c> via
/// <c>InternalsVisibleTo</c>.
/// </summary>
internal interface IRouteRepository
{
    Task<RouteRecord> CreateAsync(RouteRecord record, CancellationToken cancellationToken = default);

    Task<RouteRecord?> GetAsync(string tenantId, string routeId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RouteRecord>> ListByTenantAsync(string tenantId, CancellationToken cancellationToken = default);

    /// <summary>Compare-and-set against <paramref name="expectedCurrent"/> (ADR-0001 Decision 3.2).</summary>
    Task<RouteRecord> ReplaceAsync(RouteRecord expectedCurrent, RouteRecord updated, CancellationToken cancellationToken = default);
}
