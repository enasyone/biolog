namespace Biolog.Features.AuditInvestigation.Tenancy;

/// <summary>This feature's own minimal tenant-context read seam (matching every sibling feature project's shape). Implemented by an adapter over <c>Biolog.Api.Tenancy.ITenantContextAccessor</c> in <c>BioRpt003Module</c> - never referenced directly.</summary>
public interface ICurrentTenantProvider
{
    string? TenantId { get; }
}
