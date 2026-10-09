namespace Biolog.Features.TenantFeatureConfig.Domain;

/// <summary>
/// BIO-TEN-003-AC04: a clean, reusable, already-tested read seam for ANY future story that
/// needs to ask "is this tenant's module/subfeature enabled" - the same shape BIO-BIO-007's
/// own <c>IFaceSearchFeatureFlagPolicy</c> established for biometrics 1:N specifically, offered
/// here for the other five modules this story's AC04 covers but which have no consuming
/// story wired yet (card production, visitor, attendance, transport, Document Verify - see
/// docs/features/bio-ten-003/README.md "AC04 scope boundary"). Implemented by
/// <see cref="TenantFeatureConfigurationService"/> itself and registered under this
/// interface too (<c>BioTen003Module</c>), so a later consuming story depends on this narrow
/// interface rather than the whole service.
///
/// Every method here fails closed (BIO-TEN-003-AC04): a tenant with no configuration ever
/// written returns <c>false</c> for every module/subfeature, never a 404/exception a caller
/// could mishandle as "enabled".
/// </summary>
public interface ITenantFeatureGateway
{
    Task<bool> IsModuleEnabledAsync(string tenantId, TenantModule module, CancellationToken cancellationToken = default);

    Task<bool> IsDocumentVerifySubfeatureEnabledAsync(string tenantId, DocumentVerifySubfeature subfeature, CancellationToken cancellationToken = default);

    /// <summary>BIO-TEN-003-AC02's own flag, surfaced on this general gateway too (in addition
    /// to the dedicated, synchronous <c>IFaceSearchFeatureFlagPolicy</c> adapter BIO-BIO-007
    /// actually consumes) for any other async caller that needs it.</summary>
    Task<bool> IsFace1NIdentificationEnabledAsync(string tenantId, CancellationToken cancellationToken = default);
}
