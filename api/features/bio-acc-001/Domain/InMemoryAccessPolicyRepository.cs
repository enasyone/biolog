using System.Collections.Concurrent;

namespace Biolog.Features.AccessPolicy.Domain;

/// <summary>
/// A single, process-lifetime, cross-tenant dictionary standing in for the per-tenant database
/// ADR-0002 Decision 4.1 describes, keyed by <c>(TenantId, PolicyKey)</c>. See
/// <see cref="IAccessPolicyRepository"/> for why this class is <c>internal</c> and what
/// belt-and-braces validation it applies independently of <see cref="AccessPolicyService"/>.
/// </summary>
internal sealed class InMemoryAccessPolicyRepository : IAccessPolicyRepository
{
    private readonly ConcurrentDictionary<(string TenantId, string PolicyKey), Domain.AccessPolicy> _policies = new();

    public Task<Domain.AccessPolicy?> GetAsync(string tenantId, string policyKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _policies.TryGetValue((tenantId, policyKey), out var policy);
        return Task.FromResult(policy);
    }

    public Task<IReadOnlyList<Domain.AccessPolicy>> ListAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<Domain.AccessPolicy> items =
        [
            .. _policies.Values.Where(p => p.TenantId == tenantId).OrderBy(p => p.PolicyKey, StringComparer.Ordinal),
        ];
        return Task.FromResult(items);
    }

    public Task<Domain.AccessPolicy> CreateAsync(Domain.AccessPolicy policy, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (policy.ConcurrencyVersion != 1 || policy.Latest.VersionNumber != 1 || policy.Latest.Status != AccessPolicyStatus.Draft)
        {
            throw new InvalidOperationException($"a newly created access policy must start as a single DRAFT version at concurrency version 1: {policy.PolicyKey}");
        }

        var key = (policy.TenantId, policy.PolicyKey);
        if (!_policies.TryAdd(key, policy))
        {
            throw AccessPolicyError.PolicyAlreadyExists(policy.PolicyKey);
        }

        return Task.FromResult(policy);
    }

    public Task<Domain.AccessPolicy> ReplaceAsync(Domain.AccessPolicy expected, Domain.AccessPolicy updated, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = (expected.TenantId, expected.PolicyKey);
        if (!_policies.TryGetValue(key, out var current) || current.TenantId != expected.TenantId)
        {
            throw AccessPolicyError.PolicyNotFound(expected.PolicyKey);
        }

        if (updated.TenantId != expected.TenantId || updated.PolicyKey != expected.PolicyKey)
        {
            // Unreachable via `expected with { ... }` (these fields are get-only) - catches two
            // unrelated instances being passed together, mirroring every sibling repository's
            // identical dual-boundary check.
            throw AccessPolicyError.PolicyIdentityImmutable(expected.PolicyKey);
        }

        if (!_policies.TryUpdate(key, updated, expected))
        {
            var latest = _policies.TryGetValue(key, out var p) ? p : current;
            throw AccessPolicyError.ConcurrencyConflict(expected.PolicyKey, expected.ConcurrencyVersion, latest.ConcurrencyVersion);
        }

        return Task.FromResult(updated);
    }
}
