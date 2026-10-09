using System.Collections.Concurrent;

namespace Biolog.Features.Liveness.Domain;

/// <summary>
/// BIO-BIO-008-AC03. Every (tenant, workflow) pair defaults to NOT required until explicitly
/// configured - the same "unconfigured means not yet opted in" default BIO-BIO-007's own tenant
/// feature-flag policy uses, so a newly-provisioned tenant is never surprised by a liveness gate
/// it never asked for.
/// </summary>
internal sealed class InMemoryLivenessPolicy : ILivenessPolicy
{
    private readonly ConcurrentDictionary<(string TenantId, string Workflow), bool> _required = new();

    public Task<bool> IsRequiredAsync(string tenantId, string workflow, CancellationToken cancellationToken) =>
        Task.FromResult(_required.TryGetValue((tenantId, workflow), out var required) && required);

    public void SetRequired(string tenantId, string workflow) => _required[(tenantId, workflow)] = true;

    public void SetNotRequired(string tenantId, string workflow) => _required[(tenantId, workflow)] = false;
}
