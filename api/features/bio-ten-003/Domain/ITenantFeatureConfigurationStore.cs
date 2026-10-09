namespace Biolog.Features.TenantFeatureConfig.Domain;

/// <summary>
/// The tenant-plane feature-configuration store (BIO-TEN-003-AC03: versioned). Backed by
/// <see cref="InMemoryTenantFeatureConfigurationStore"/> for this story - the identical
/// later-persistence seam BIO-TEN-001's own <c>ITenantRegistry</c> already established for
/// this codebase; a later, dedicated persistence story swaps a real implementation in without
/// touching <see cref="TenantFeatureConfigurationService"/> or its endpoints.
/// </summary>
public interface ITenantFeatureConfigurationStore
{
    /// <summary>Null if no configuration has ever been written for this tenant - the caller
    /// (<see cref="TenantFeatureConfigurationService.GetEffectiveAsync"/>) is responsible for
    /// turning that into the fail-closed default, never this store.</summary>
    Task<TenantFeatureConfiguration?> GetAsync(string tenantId, CancellationToken cancellationToken = default);

    /// <summary>The first-ever write for this tenant. Throws if one already exists - the
    /// caller must use <see cref="ReplaceAsync"/> once a configuration exists.</summary>
    Task<TenantFeatureConfiguration> CreateAsync(TenantFeatureConfiguration config, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomic compare-and-swap on the whole record (ADR-0001 §3.1's optimistic concurrency
    /// token). Fails with <see cref="TenantFeatureConfigurationError.ConcurrencyConflict"/>
    /// when <paramref name="expected"/> no longer matches what is stored.
    /// </summary>
    Task<TenantFeatureConfiguration> ReplaceAsync(TenantFeatureConfiguration expected, TenantFeatureConfiguration updated, CancellationToken cancellationToken = default);
}
