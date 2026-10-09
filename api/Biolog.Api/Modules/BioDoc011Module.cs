using Biolog.Api.Tenancy;
using Biolog.Features.DocumentTemplateEditor;
using Biolog.Features.DocumentTemplateEditor.Api;
using Biolog.Features.DocumentTemplateEditor.Audit;
using Biolog.Features.DocumentTemplateEditor.Domain;
using Biolog.Features.DocumentTemplateEditor.Tenancy;

namespace Biolog.Api.Modules;

/// <summary>
/// Tenant visual document-template editor integration contract (BIO-DOC-011), registered the
/// same way every sibling module wraps its feature project.
///
/// <see cref="CurrentTenantProviderAdapter"/> only reads the already-shipped
/// <see cref="ITenantContextAccessor"/> - the identical shape BIO-CRD-008's own module uses for
/// its own <c>ICurrentTenantProvider</c>.
///
/// <b>Round-2 fix (Zainab, ZB-DOC011-01, Critical).</b> Round 1's own reasoning here claimed no
/// authentication story had landed yet to make a real permission check meaningful - true when
/// BIO-DOC-011 was first dispatched, but stale by the time it reached review: BIO-INT-001 is
/// integrated and multiple accepted W12 features (BIO-INT-002, BIO-MOB-004/005, BIO-TRN-001,
/// BIO-VIS-001/002, BIO-ATT-001/002, BIO-ACC-001/002/003) already wire the same provenance-plus-
/// permission seam this module now adds. <see cref="PermissionGatewayAdapter"/> wraps BIO-IDN-006's
/// own already-registered <c>PermissionService.ActorHasPermissionAsync</c> directly - reachable
/// here only because that project's own csproj grants <c>Biolog.Api</c> internals visibility, the
/// same cross-story gateway pattern BIO-INT-002's own module already established. No
/// <c>ProjectReference</c> is added to the feature project itself - the gateway interface
/// (<c>Domain.IPermissionGateway</c>) is this feature's own narrow seam, exactly like
/// <c>ICurrentTenantProvider</c> above.
/// </summary>
public sealed class BioDoc011Module : IBiologModule
{
    public string Name => "DocumentTemplateEditor";
    public IReadOnlyCollection<string> DependsOn => ["Tenant", "Rbac"];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ICurrentTenantProvider>(sp => new CurrentTenantProviderAdapter(sp.GetRequiredService<ITenantContextAccessor>()));

        services.AddSingleton<IDocumentTemplateDraftRepository, InMemoryDocumentTemplateDraftRepository>();
        services.AddSingleton<IDocumentTemplateDraftAuditSink, InMemoryDocumentTemplateDraftAuditSink>();
        services.AddSingleton<DocumentTemplateDraftLockRegistry>();

        services.AddScoped<IPermissionGateway>(
            sp => new PermissionGatewayAdapter(sp.GetRequiredService<Biolog.Features.Rbac.PermissionService>()));

        services.AddScoped<DocumentTemplateDraftService>();

        services.AddSingleton(_ => ActorProvenanceProvisioning.LoadFromConfiguration(configuration));
        services.AddSingleton<ActorProvenanceGate>();
    }

    public void Configure(IApplicationBuilder app)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        DocumentTemplateDraftEndpoints.Map(endpoints);
    }

    private sealed class CurrentTenantProviderAdapter(ITenantContextAccessor accessor) : ICurrentTenantProvider
    {
        public string? TenantId => accessor.Context?.TenantId;
    }

    private sealed class PermissionGatewayAdapter(Biolog.Features.Rbac.PermissionService permissionService) : IPermissionGateway
    {
        public Task<bool> ActorHasPermissionAsync(string tenantId, string actorId, string permissionKey, CancellationToken cancellationToken = default) =>
            permissionService.ActorHasPermissionAsync(tenantId, actorId, permissionKey, cancellationToken);
    }
}
