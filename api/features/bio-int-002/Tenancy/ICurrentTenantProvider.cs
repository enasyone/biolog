namespace Biolog.Features.Integrations.Tenancy;

/// <summary>This feature's own narrow view of the ambient tenant - implemented in <c>Biolog.Api.Modules.BioInt002Module</c> by reading the already-shipped tenant middleware's resolved context, the same adapter shape every sibling module uses.</summary>
public interface ICurrentTenantProvider
{
    string? TenantId { get; }
}
