namespace Biolog.Features.PersonProfileRegistry.Tenancy;

/// <summary>
/// This feature's own minimal seam onto ADR-0002's resolved tenant context - identical in
/// shape and rationale to BIO-IDN-001's <c>Tenancy.ICurrentTenantProvider</c> (that interface's
/// header explains the one-way-dependency reasoning in full). Duplicated here rather than
/// shared, matching this codebase's established convention of each feature project owning its
/// own small primitives rather than a cross-feature-project reference.
/// </summary>
public interface ICurrentTenantProvider
{
    string? TenantId { get; }
}
