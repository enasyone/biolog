namespace Biolog.Features.TenantFeatureConfig.Domain;

/// <summary>
/// Round-2 remediation (ZB-TEN003-01/BIO-TEN-003-F01, Critical). Verifies a target tenant id
/// actually exists in BIO-TEN-001's own control-plane registry before this feature reads or
/// writes a configuration for it - closing the "configuration can be created for a nonexistent
/// tenant" finding. Implemented in <c>Biolog.Api.Modules.BioTen003Module</c> by wrapping
/// BIO-TEN-001's own already-registered <c>TenantService.GetAsync</c> directly, the same
/// cross-story read-only gateway pattern every sibling story uses. This was previously a
/// disclosed boundary ("this feature does not verify a tenant id against BIO-TEN-001's own
/// registry") - the round-1 review found that boundary itself was the defect, not merely
/// undocumented.
/// </summary>
public interface ITenantExistenceGateway
{
    Task<bool> ExistsAsync(string tenantId, CancellationToken cancellationToken = default);
}
