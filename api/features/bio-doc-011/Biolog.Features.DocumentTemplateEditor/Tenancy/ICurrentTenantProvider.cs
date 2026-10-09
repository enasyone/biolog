namespace Biolog.Features.DocumentTemplateEditor.Tenancy;

/// <summary>This feature's own minimal tenant-context read seam (AC04-style isolation, matching every sibling feature project's shape - see BIO-CRD-008's own identical interface). Implemented by an adapter over <c>Biolog.Api.Tenancy.ITenantContextAccessor</c> in <c>BioDoc011Module</c> - never referenced directly.</summary>
public interface ICurrentTenantProvider
{
    string? TenantId { get; }
}
