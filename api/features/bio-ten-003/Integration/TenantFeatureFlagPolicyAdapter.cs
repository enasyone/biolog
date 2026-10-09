using Biolog.Features.FaceSearch.Domain;

namespace Biolog.Features.TenantFeatureConfig.Integration;

/// <summary>
/// BIO-TEN-003-AC02: the real, persisted-in-this-process implementation of BIO-BIO-007's own
/// <see cref="IFaceSearchFeatureFlagPolicy"/> extension seam, replacing
/// <c>SyntheticFaceSearchFeatureFlagPolicy</c> in <c>BioBio007Module.RegisterServices</c> (one
/// line changed there - see that file's own header for the pre-approval record and
/// docs/features/bio-ten-003/README.md "AC02: the BIO-BIO-007 swap" for the full account).
///
/// <see cref="IFaceSearchFeatureFlagPolicy.IsEnabledForTenant"/> is called synchronously (not
/// awaited) by <c>FaceSearchService.SearchAsync</c>, so this adapter cannot itself await a
/// store read - it delegates to <see cref="TenantFeatureConfigurationService.IsFace1NIdentificationEnabledSync"/>,
/// the in-memory cache that service keeps in lockstep with every committed write. Fail-closed
/// by construction: a tenant never written to (or not yet reflected in this process's cache)
/// resolves to <c>false</c>, exactly the same default <c>SyntheticFaceSearchFeatureFlagPolicy</c>
/// already gave every tenant before this story.
/// </summary>
public sealed class TenantFeatureFlagPolicyAdapter(TenantFeatureConfigurationService service) : IFaceSearchFeatureFlagPolicy
{
    public bool IsEnabledForTenant(string tenantId) => service.IsFace1NIdentificationEnabledSync(tenantId);
}
