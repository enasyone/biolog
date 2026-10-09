namespace Biolog.Features.TenantRegistry.Domain;

/// <summary>
/// The control-plane tenant store (BIO-TEN-001-AC01). Deliberately takes no tenant context
/// anywhere on this interface: control-plane work resolves no tenant (ADR-0002 §1.4), so
/// there is no parameter here a caller could even attempt to pass one through.
///
/// Backed by <see cref="InMemoryTenantRegistry"/> for this story. No database/migration
/// story has wired MySQL (or any ORM) into <c>api/Biolog.Api</c> yet - <c>Biolog.Api.csproj</c>
/// carries no such package reference - and ADR-0005 requires a real schema change to be
/// proposed to Kemi and assigned a sequence number, not authored unilaterally by a single
/// feature story. This interface is the seam a later, dedicated persistence story swaps a
/// real implementation into without touching <see cref="TenantService"/> or the endpoints.
/// </summary>
public interface ITenantRegistry
{
    Task<Tenant> CreateAsync(Tenant tenant, CancellationToken cancellationToken = default);

    Task<Tenant?> GetAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ordered by <c>id</c> ascending - UUIDv7's leading timestamp bits make that identical
    /// to creation order (ADR-0001 §1.3). <paramref name="afterId"/> is the last id the
    /// caller already saw (decoded from the opaque cursor at the API boundary, not here).
    /// </summary>
    Task<(IReadOnlyList<Tenant> Items, bool HasMore)> ListAsync(
        int limit, string? afterId, TenantStatus? statusFilter, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomic compare-and-swap on the whole record (ADR-0001 §3.1's optimistic concurrency
    /// token). Fails with <see cref="TenantRegistryError.ConcurrencyConflict"/> when
    /// <paramref name="expected"/> no longer matches what is stored.
    /// </summary>
    Task<Tenant> ReplaceAsync(Tenant expected, Tenant updated, CancellationToken cancellationToken = default);
}
