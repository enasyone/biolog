using Biolog.Api.Tenancy;
using Biolog.Features.CardObjects;
using Biolog.Features.CardObjects.Api;
using Biolog.Features.CardObjects.Domain;
using Biolog.Features.CardObjects.Tenancy;

namespace Biolog.Api.Modules;

/// <summary>
/// Dynamic barcode/QR/signature/card objects (BIO-CRD-004), registered the same way every
/// sibling module wraps its feature project - written directly here, in
/// <c>Biolog.Api.Modules</c>, from the start, so the dependency stays one-way.
///
/// The barcode-profile catalogue/validator endpoints register no services (pure, static
/// reference data, same as <see cref="BioCrd002Module"/>/<see cref="BioCrd003Module"/>).
/// Signature-asset versioning is real, tenant-scoped state: <see cref="CurrentTenantProviderAdapter"/>
/// only reads the already-shipped <see cref="ITenantContextAccessor"/> (identical shape to
/// every sibling module's own adapter), and <see cref="InMemorySignatureAssetRepository"/> is
/// registered Singleton so appended versions survive across requests within this process - the
/// same "in-memory placeholder, not the real migration-backed store" seam BIO-CRD-001's own
/// <c>BrandAssetsModule</c> established.
///
/// "Dependencies: BIO-CRD-002" in the story's own metadata is build-wave sequencing (both
/// stories share the `card-designer` conflict group), not a runtime dependency - BIO-CRD-002's
/// own types are never referenced anywhere in this backend feature.
///
/// Depends on <see cref="TenantModule"/> (for <see cref="TenantContextMiddleware"/> to have
/// already run before signature-asset endpoints resolve a tenant).
/// </summary>
public sealed class BioCrd004Module : IBiologModule
{
    public string Name => "CardObjects";
    public IReadOnlyCollection<string> DependsOn => ["Tenant"];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ICurrentTenantProvider>(sp => new CurrentTenantProviderAdapter(sp.GetRequiredService<ITenantContextAccessor>()));
        services.AddSingleton<ISignatureAssetRepository, InMemorySignatureAssetRepository>();
        services.AddScoped<SignatureAssetService>();
    }

    public void Configure(IApplicationBuilder app)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        BarcodeProfileEndpoints.Map(endpoints);
        SignatureAssetEndpoints.Map(endpoints);
    }

    private sealed class CurrentTenantProviderAdapter(ITenantContextAccessor accessor) : ICurrentTenantProvider
    {
        public string? TenantId => accessor.Context?.TenantId;
    }
}
