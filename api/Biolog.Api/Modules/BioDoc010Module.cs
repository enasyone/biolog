using Biolog.Features.DocumentTemplatePack.Api;
using Biolog.Features.DocumentTemplatePack.Audit;
using Biolog.Features.DocumentTemplatePack.Store;

namespace Biolog.Api.Modules;

/// <summary>
/// Nigeria Document Intelligence Pack v1 integration contract (BIO-DOC-010), registered the same
/// way every sibling module wraps its feature project.
///
/// No cross-story runtime gateway is needed: this story's own declared dependencies (BIO-DOC-006,
/// BIO-DOC-009) are consumed entirely inside <c>Biolog.Features.DocumentTemplatePack</c> itself,
/// via its own <c>ProjectReference</c>s, not through another module's already-registered service -
/// this story's own <see cref="DocumentTemplatePackService"/> owns a private, unshared
/// <c>DocumentIntelligenceRegistry</c> instance rather than reaching for one BIO-DOC-006 never
/// registers into DI (BIO-DOC-006 stayed a pure domain library with no HTTP module of its own).
///
/// BIO-DOC-010-AC05: <see cref="DocumentTemplatePackEndpoints"/> maps
/// <c>POST /api/v1/documents/identify</c> - the exact route
/// <c>web/features/documents/live.ts</c>'s already-reviewed <c>LIVE_ENDPOINTS['document-identification']</c>
/// targets.
/// </summary>
public sealed class BioDoc010Module : IBiologModule
{
    public string Name => "DocumentTemplatePack";
    public IReadOnlyCollection<string> DependsOn => Array.Empty<string>();

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IDocumentTemplatePackStore, InMemoryDocumentTemplatePackStore>();
        services.AddSingleton<IDocumentTemplateSampleStore, InMemoryDocumentTemplateSampleStore>();
        services.AddSingleton<IDocumentTemplatePackAuditSink, InMemoryDocumentTemplatePackAuditSink>();
        services.AddSingleton<DocumentTemplatePackService>();
    }

    public void Configure(IApplicationBuilder app)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        DocumentTemplatePackEndpoints.Map(endpoints);
    }
}
