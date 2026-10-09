namespace Biolog.Features.Rbac.Domain;

/// <summary>
/// Storage for <see cref="SensitiveActionRule"/>, keyed by <c>(TenantId, ActionKey)</c>.
/// <c>internal</c> from the start. <see cref="UpsertAsync"/> is unconditional, same reasoning as
/// <see cref="IRolePermissionGrantRepository.UpsertAsync"/>.
/// </summary>
internal interface ISensitiveActionRuleRepository
{
    Task<SensitiveActionRule?> GetAsync(string tenantId, string actionKey, CancellationToken cancellationToken = default);

    Task<SensitiveActionRule> UpsertAsync(SensitiveActionRule rule, CancellationToken cancellationToken = default);
}
