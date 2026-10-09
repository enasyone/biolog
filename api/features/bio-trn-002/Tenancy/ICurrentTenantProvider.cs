namespace Biolog.Features.TransportEvents.Tenancy;

/// <summary>
/// This feature's own minimal seam onto ADR-0002's resolved tenant context - deliberately NOT a
/// reference to <c>Biolog.Api.Tenancy.ITenantContextAccessor</c> (one-way dependency,
/// <c>scripts/check-module-project-references.sh</c>). <c>null</c> means no channel resolved a
/// tenant for this request (ADR-0002 Decision 2: no implicit or default tenant, ever).
/// </summary>
public interface ICurrentTenantProvider
{
    string? TenantId { get; }
}
