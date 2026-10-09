namespace Biolog.Features.AccessPolicy.Domain;

/// <summary>
/// Storage for <see cref="Domain.AccessPolicy"/>, keyed by <c>(TenantId, PolicyKey)</c>.
/// <c>internal</c> from the start - <c>InternalsVisibleTo</c> grants access to <c>Biolog.Api</c>
/// (DI registration) and this project's own test assembly.
/// <see cref="InMemoryAccessPolicyRepository"/> independently re-validates identity immutability
/// and the compare-and-set at the persistence boundary - the same dual-boundary discipline every
/// sibling repository in this codebase's identity wave applies.
/// </summary>
internal interface IAccessPolicyRepository
{
    Task<Domain.AccessPolicy?> GetAsync(string tenantId, string policyKey, CancellationToken cancellationToken = default);

    /// <summary>Every policy in the tenant, in creation order - AC04's audit-visible catalogue.</summary>
    Task<IReadOnlyList<Domain.AccessPolicy>> ListAsync(string tenantId, CancellationToken cancellationToken = default);

    Task<Domain.AccessPolicy> CreateAsync(Domain.AccessPolicy policy, CancellationToken cancellationToken = default);

    /// <summary>Compare-and-set against <paramref name="expected"/> (ADR-0001 Decision 3.2).</summary>
    Task<Domain.AccessPolicy> ReplaceAsync(Domain.AccessPolicy expected, Domain.AccessPolicy updated, CancellationToken cancellationToken = default);
}
