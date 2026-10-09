using Biolog.Api.Tenancy;
using Biolog.Features.CardTemplates;
using Biolog.Features.CardTemplates.Api;
using Biolog.Features.CardTemplates.Audit;
using Biolog.Features.CardTemplates.Domain;
using Biolog.Features.CardTemplates.Tenancy;

namespace Biolog.Api.Modules;

/// <summary>
/// Published card-template lifecycle and provenance (BIO-CRD-008), registered the same way
/// every sibling card module wraps its feature project - written directly here, in
/// <c>Biolog.Api.Modules</c>, from the start, so the dependency stays one-way.
///
/// <see cref="CurrentTenantProviderAdapter"/> only reads the already-shipped
/// <see cref="ITenantContextAccessor"/> (identical shape to every sibling module's own adapter).
///
/// <b><see cref="SignatureAssetGatewayAdapter"/> is the real cross-story dependency.</b> It
/// wraps BIO-CRD-004's own already-registered <c>ISignatureAssetRepository</c> directly -
/// reachable here only because that project's own csproj grants
/// <c>InternalsVisibleTo Biolog.Api</c> is NOT needed for this call (the repository interface
/// and <c>SignatureAsset</c> itself are both public), unlike BIO-CRD-007's own
/// <c>ICardIssueGateway</c> which does need it for an internal application service.
///
/// <b>Watermark/barcode/format vocabulary is mirrored, not gatewayed</b> - see
/// <c>Biolog.Features.CardTemplates.Domain.CardFormatKind</c>'s own header for why.
///
/// Depends on <see cref="TenantModule"/> and <see cref="BioCrd004Module"/> (registered as
/// <c>"CardObjects"</c>, for <see cref="Biolog.Features.CardObjects.Domain.ISignatureAssetRepository"/>
/// to already be registered). "Dependencies: BIO-CRD-003" in the story's own metadata is
/// build-wave sequencing (both stories share the `card-designer` conflict group) - BIO-CRD-003's
/// own types are never referenced anywhere in this backend feature, only its wire vocabulary
/// mirrored as this project's own whitelist.
/// </summary>
public sealed class BioCrd008Module : IBiologModule
{
    public string Name => "CardTemplates";
    public IReadOnlyCollection<string> DependsOn => ["Tenant", "CardObjects"];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ICurrentTenantProvider>(sp => new CurrentTenantProviderAdapter(sp.GetRequiredService<ITenantContextAccessor>()));
        services.AddSingleton<ISignatureAssetGateway>(
            sp => new SignatureAssetGatewayAdapter(sp.GetRequiredService<Biolog.Features.CardObjects.Domain.ISignatureAssetRepository>()));

        services.AddSingleton<ICardTemplateRepository, InMemoryCardTemplateRepository>();
        services.AddSingleton<ICardTemplateAuditSink, InMemoryCardTemplateAuditSink>();

        services.AddScoped<CardTemplateService>();
    }

    public void Configure(IApplicationBuilder app)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        CardTemplateEndpoints.Map(endpoints);
    }

    private sealed class CurrentTenantProviderAdapter(ITenantContextAccessor accessor) : ICurrentTenantProvider
    {
        public string? TenantId => accessor.Context?.TenantId;
    }

    private sealed class SignatureAssetGatewayAdapter(Biolog.Features.CardObjects.Domain.ISignatureAssetRepository repository) : ISignatureAssetGateway
    {
        public bool Exists(string tenantId, string lineageId, int version) =>
            repository.GetVersion(tenantId, lineageId, version) is not null;
    }
}
