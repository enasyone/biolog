namespace Biolog.Features.VisitorCredentials.Tenancy;

/// <summary>This feature's own narrow view of the ambient tenant - implemented in <c>Biolog.Api.Modules.BioVis002Module</c> by reading the already-shipped tenant middleware's resolved context, the same adapter shape every sibling module uses.</summary>
public interface ICurrentTenantProvider
{
    string? TenantId { get; }
}
