namespace Biolog.Features.ReportBuilder.Tenancy;

/// <summary>This feature's own minimal tenant-context read seam (AC03), the same shape every sibling feature project already establishes. Implemented by an adapter over <c>Biolog.Api.Tenancy.ITenantContextAccessor</c> in <c>BioRpt002Module</c> - never referenced directly.</summary>
public interface ICurrentTenantProvider
{
    string? TenantId { get; }
}
