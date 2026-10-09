using System.Collections.Concurrent;

namespace Biolog.Features.InstitutionalIdentifiers.Domain;

/// <summary>In-process store for BIO-IDN-003-AC02's per-type uniqueness rules. See <see cref="IIdentifierUniquenessRuleRepository"/>.</summary>
internal sealed class InMemoryIdentifierUniquenessRuleRepository : IIdentifierUniquenessRuleRepository
{
    private readonly ConcurrentDictionary<(string TenantId, IdentifierType Type, string? CustomLabel), IdentifierUniquenessRule> _rules = new();

    public Task<IdentifierUniquenessRule?> GetAsync(string tenantId, IdentifierType type, string? customLabel, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _rules.TryGetValue((tenantId, type, customLabel), out var rule);
        return Task.FromResult(rule);
    }

    public Task<IdentifierUniquenessRule> UpsertAsync(IdentifierUniquenessRule rule, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _rules[(rule.TenantId, rule.Type, rule.CustomLabel)] = rule;
        return Task.FromResult(rule);
    }
}
