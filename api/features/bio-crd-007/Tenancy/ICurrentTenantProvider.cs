namespace Biolog.Features.CardProduction.Tenancy;

/// <summary>This feature's own minimal seam onto ADR-0002's resolved tenant context - identical in shape and rationale to every sibling story's own <c>Tenancy.ICurrentTenantProvider</c>.</summary>
public interface ICurrentTenantProvider
{
    string? TenantId { get; }
}
