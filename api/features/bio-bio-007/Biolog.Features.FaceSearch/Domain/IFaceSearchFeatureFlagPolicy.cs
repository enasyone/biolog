namespace Biolog.Features.FaceSearch.Domain;

/// <summary>
/// AC02: "1:N requires explicit tenant feature enablement." A tenant-wide, fail-closed switch -
/// deliberately distinct from an actor's own permission (that would be a PER-CALLER concern; this
/// is a PER-TENANT one, gating whether 1:N face search exists for this tenant at all, regardless
/// of who is calling). <see cref="Service.FaceSearchService.SearchAsync"/> checks this BEFORE the
/// gallery is even read; a tenant that has never explicitly enabled the feature can never reach a
/// search result no matter who calls it or what they hold.
/// </summary>
public interface IFaceSearchFeatureFlagPolicy
{
    bool IsEnabledForTenant(string tenantId);
}
