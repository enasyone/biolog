using System.Collections.Concurrent;

namespace Biolog.Features.Rbac.Domain;

/// <summary>A single, process-lifetime, cross-tenant dictionary keyed by <c>(TenantId, ActionKey)</c>. See <see cref="ISensitiveActionRuleRepository"/>.</summary>
internal sealed class InMemorySensitiveActionRuleRepository : ISensitiveActionRuleRepository
{
    private readonly ConcurrentDictionary<(string TenantId, string ActionKey), SensitiveActionRule> _rules = new();

    public Task<SensitiveActionRule?> GetAsync(string tenantId, string actionKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _rules.TryGetValue((tenantId, actionKey), out var rule);
        return Task.FromResult(rule);
    }

    public Task<SensitiveActionRule> UpsertAsync(SensitiveActionRule rule, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _rules[(rule.TenantId, rule.ActionKey)] = rule;
        return Task.FromResult(rule);
    }
}
