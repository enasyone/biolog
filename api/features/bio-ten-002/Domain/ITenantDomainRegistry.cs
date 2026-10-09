namespace Biolog.Features.TenantDomainRegistry.Domain;

/// <summary>
/// The control-plane tenant-domain store (BIO-TEN-002-AC01), keyed by hostname (see
/// <see cref="TenantDomain"/>'s header for why there is no separate id). Backed by
/// <see cref="InMemoryTenantDomainRegistry"/> for this story; a later persistence story
/// swaps a real implementation in without touching <see cref="TenantDomainService"/> or the
/// endpoints, the same seam BIO-TEN-001's <c>ITenantRegistry</c> establishes.
/// </summary>
public interface ITenantDomainRegistry
{
    /// <summary>
    /// Atomically creates a new record keyed by <c>tenant.Hostname</c>. Fails with
    /// <see cref="TenantDomainError.DuplicateHostname"/> if that hostname already has a
    /// record - this is BIO-TEN-002-AC04's actual enforcement point: the store's key
    /// structure makes two records ever sharing one hostname unrepresentable, independent of
    /// whatever locking the caller does or does not hold.
    /// </summary>
    Task<TenantDomain> CreateAsync(TenantDomain domain, CancellationToken cancellationToken = default);

    Task<TenantDomain?> GetByHostnameAsync(string hostname, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every domain currently registered to <paramref name="tenantId"/>, in no particular
    /// order. A tenant's domain count is small (a handful of custom hostnames, not a
    /// high-cardinality table), so this story does not paginate it - see
    /// docs/features/bio-ten-002/README.md's scope decisions.
    /// </summary>
    Task<IReadOnlyList<TenantDomain>> ListByTenantAsync(string tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomic compare-and-swap on the whole record (ADR-0001 §3.1's optimistic concurrency
    /// token). Fails with <see cref="TenantDomainError.ConcurrencyConflict"/> when
    /// <paramref name="expected"/> no longer matches what is stored.
    /// </summary>
    Task<TenantDomain> ReplaceAsync(TenantDomain expected, TenantDomain updated, CancellationToken cancellationToken = default);
}
