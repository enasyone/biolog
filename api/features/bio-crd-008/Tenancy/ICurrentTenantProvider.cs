namespace Biolog.Features.CardTemplates.Tenancy;

/// <summary>This feature's own minimal tenant-context read seam (AC03-style isolation, matching every sibling feature project's shape). Implemented by an adapter over <c>Biolog.Api.Tenancy.ITenantContextAccessor</c> in <c>BioCrd008Module</c> - never referenced directly.</summary>
public interface ICurrentTenantProvider
{
    string? TenantId { get; }
}
