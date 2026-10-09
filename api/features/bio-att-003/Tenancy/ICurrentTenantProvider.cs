namespace Biolog.Features.WorkforceAttendance.Tenancy;

/// <summary>
/// This feature's own minimal seam onto ADR-0002's resolved tenant context - deliberately NOT a
/// reference to <c>Biolog.Api.Tenancy.ITenantContextAccessor</c>, since this project has no
/// <c>ProjectReference</c> to <c>Biolog.Api.csproj</c>. Every identity-wave feature in this
/// codebase defines this identical narrow interface for itself, copied from BIO-ATT-001's own
/// identically-shaped interface. <c>null</c> means no channel resolved a tenant for this request
/// (ADR-0002 Decision 2: no implicit or default tenant, ever).
/// </summary>
public interface ICurrentTenantProvider
{
    string? TenantId { get; }
}
