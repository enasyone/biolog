using Biolog.Features.AuthenticityAssessment.Api;
using Biolog.Features.AuthenticityAssessment.Audit;
using Biolog.Features.AuthenticityAssessment.Model;

namespace Biolog.Api.Modules;

/// <summary>
/// Authenticity Assessment orchestrator integration contract (BIO-DOC-009), registered the same
/// way every sibling module wraps its feature project - written directly here, in
/// <c>Biolog.Api.Modules</c>, from the start (never inside <c>api/features/bio-doc-009/**</c>
/// first), so the dependency stays one-way.
///
/// No cross-story runtime gateway is needed (unlike <see cref="BioPho002Module"/>'s three): this
/// story's own declared dependencies (BIO-DOC-006, BIO-DOC-008) are consumed entirely inside
/// <c>Biolog.Features.AuthenticityAssessment</c> itself, via its own <c>ProjectReference</c>s, not
/// through another module's already-registered service.
///
/// BIO-DOC-009-AC06: <see cref="AuthenticityEndpoints"/> maps
/// <c>POST /api/v1/documents/authenticity</c> - the exact route
/// <c>web/features/documents/live.ts</c>'s already-reviewed <c>LIVE_ENDPOINTS.authenticity</c>
/// targets - plus its own read/override routes.
/// </summary>
public sealed class BioDoc009Module : IBiologModule
{
    public string Name => "AuthenticityAssessment";
    public IReadOnlyCollection<string> DependsOn => Array.Empty<string>();

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IAuthenticityAssessmentStore, InMemoryAuthenticityAssessmentStore>();
        services.AddSingleton<IAuthenticityAssessmentAuditSink, InMemoryAuthenticityAssessmentAuditSink>();
        services.AddSingleton<AuthenticityAssessmentService>();
    }

    public void Configure(IApplicationBuilder app)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        AuthenticityEndpoints.Map(endpoints);
    }
}
