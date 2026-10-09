namespace Biolog.Features.Attendance.Tenancy;

/// <summary>
/// This feature's own minimal seam onto ADR-0002's resolved tenant context - deliberately NOT a
/// reference to <c>Biolog.Api.Tenancy.ITenantContextAccessor</c>, since this project has no
/// <c>ProjectReference</c> to <c>Biolog.Api.csproj</c> (one-way dependency,
/// <c>scripts/check-module-project-references.sh</c>). Every identity-wave feature in this
/// codebase defines this identical narrow interface for itself rather than sharing one - see
/// BIO-IDN-001's own <c>Tenancy/ICurrentTenantProvider.cs</c> for the full rationale, ported
/// verbatim here. <c>null</c> means no channel resolved a tenant for this request (ADR-0002
/// Decision 2: no implicit or default tenant, ever) - every mutating/reading method in this
/// feature fails closed on <c>null</c>.
/// </summary>
public interface ICurrentTenantProvider
{
    string? TenantId { get; }
}
