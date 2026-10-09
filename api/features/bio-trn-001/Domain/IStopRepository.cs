namespace Biolog.Features.Transport.Domain;

/// <summary>Storage for <see cref="StopRecord"/>. Same tenant-scoping and <c>internal</c> discipline as <see cref="IRouteRepository"/>. Read in-process by <c>RouteService</c> (no cross-project gateway - Stop lives in this same feature) to existence/active-check a route's own <see cref="RouteStopEntry.StopId"/> references.</summary>
internal interface IStopRepository
{
    Task<StopRecord> CreateAsync(StopRecord record, CancellationToken cancellationToken = default);

    Task<StopRecord?> GetAsync(string tenantId, string stopId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StopRecord>> ListByTenantAsync(string tenantId, CancellationToken cancellationToken = default);

    Task<StopRecord> ReplaceAsync(StopRecord expectedCurrent, StopRecord updated, CancellationToken cancellationToken = default);
}
