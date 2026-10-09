namespace Biolog.Features.DocumentVerifyCoordination.Tenancy;

/// <summary>
/// This feature's own zero-reference seam onto the host's resolved tenant (the same pattern
/// BIO-DOC-011's own <c>ICurrentTenantProvider</c> established): no <c>ProjectReference</c> to
/// <c>Biolog.Api</c>, so this project's own build never depends on the host. The real
/// implementation is a thin adapter in <c>Biolog.Api.Modules.BioOps005Module</c> wrapping the
/// host's already-shipped <c>ITenantContextAccessor</c> (BIO-TEN-002) directly.
/// </summary>
public interface ICurrentTenantProvider
{
    /// <summary>Null when no channel resolves a tenant for the current request (ADR-0002
    /// Decision 2: no implicit or default tenant, ever).</summary>
    string? TenantId { get; }
}
