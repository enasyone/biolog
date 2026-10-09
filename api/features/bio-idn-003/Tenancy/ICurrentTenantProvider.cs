namespace Biolog.Features.InstitutionalIdentifiers.Tenancy;

/// <summary>
/// This feature's own minimal seam onto ADR-0002's resolved tenant context - identical in
/// shape and rationale to BIO-IDN-001's and BIO-IDN-002's own <c>Tenancy.ICurrentTenantProvider</c>.
/// Duplicated here rather than shared, matching this codebase's established convention of each
/// feature project owning its own small primitives rather than a cross-feature-project
/// reference.
/// </summary>
public interface ICurrentTenantProvider
{
    string? TenantId { get; }
}
