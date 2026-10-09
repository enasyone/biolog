using Biolog.Features.CardObjects.Domain;
using Biolog.Features.CardObjects.Identifiers;
using Biolog.Features.CardObjects.Tenancy;

namespace Biolog.Features.CardObjects;

/// <summary>
/// Application service for BIO-CRD-004-AC04 (versioned signature assets). Tenant-scoped
/// read/append over <see cref="ISignatureAssetRepository"/> - lean by design, following
/// BIO-CRD-001's own <c>BrandAsset</c> precedent rather than BIO-PHO-001's heavier
/// audit-atomicity/lock-registry apparatus (see the repository's own header for why).
///
/// <b>Tenant context.</b> Reads <see cref="ICurrentTenantProvider.TenantId"/> itself, never
/// accepts one from a caller, and throws <see cref="CardObjectsError.TenantContextMissing"/>
/// when none is resolved - the same fail-closed rule every sibling tenant-plane story applies.
/// </summary>
internal sealed class SignatureAssetService(ICurrentTenantProvider tenantProvider, ISignatureAssetRepository repository)
{
    public SignatureAsset AppendVersion(string lineageId, byte[] content) =>
        repository.AppendVersion(RequireTenant(), lineageId, content, UtcInstant.Now());

    public SignatureAsset GetLatestVersion(string lineageId) =>
        repository.GetLatestVersion(RequireTenant(), lineageId) ?? throw CardObjectsError.SignatureVersionNotFound(lineageId, version: null);

    public SignatureAsset GetVersion(string lineageId, int version) =>
        repository.GetVersion(RequireTenant(), lineageId, version) ?? throw CardObjectsError.SignatureVersionNotFound(lineageId, version);

    public IReadOnlyList<SignatureAsset> ListVersions(string lineageId) =>
        repository.ListVersions(RequireTenant(), lineageId);

    private string RequireTenant() => tenantProvider.TenantId ?? throw CardObjectsError.TenantContextMissing();
}
