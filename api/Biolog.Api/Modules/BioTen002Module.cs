using Biolog.Features.TenantDomainRegistry;
using Biolog.Features.TenantDomainRegistry.Api;
using Biolog.Features.TenantDomainRegistry.Audit;
using Biolog.Features.TenantDomainRegistry.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Biolog.Api.Modules;

/// <summary>
/// BIO-TEN-002's module, registered the same way <see cref="DocumentModule"/>,
/// <see cref="DeviceRegistryModule"/> and <see cref="BioTen001Module"/> wrap their feature
/// projects: this thin adapter lives here, in <c>Biolog.Api.Modules</c>, not inside
/// <c>api/features/bio-ten-002/**</c>, so the dependency between <c>Biolog.Api</c> and the
/// feature project stays one-way (<c>scripts/check-module-project-references.sh</c>,
/// BIO-TEN-001's circular-reference finding). Depends on nothing at the DI level: this story
/// does not consume BIO-TEN-001's actual tenant registry at runtime (each story owns exactly
/// its own <c>api/features/&lt;story-key&gt;/**</c> subtree, and this story only validates the
/// SHAPE of a caller-supplied <c>tenant_id</c>, never its existence against another story's
/// store) - "Dependencies: BIO-TEN-001" in the story metadata is a build-wave/conceptual
/// sequencing note, not a runtime service dependency.
///
/// Registered in <c>Program.cs</c>'s module array, and <see cref="TenantDomainService.ResolveAsync"/>
/// is wired into <see cref="Tenancy.TenantContextMiddleware"/>'s host channel (ADR-0002
/// Decision 1.3) - see that class's header for what is and is not proven by that wiring, and
/// docs/features/bio-ten-002/README.md "BIO-TEN-002-QA-01" for why the endpoint's own host
/// source needed fixing first.
/// </summary>
public sealed class BioTen002Module : IBiologModule
{
    public string Name => "TenantDomainRegistry";
    public IReadOnlyCollection<string> DependsOn => [];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<ITenantDomainRegistry, InMemoryTenantDomainRegistry>();
        services.AddSingleton<ITenantDomainAuditSink, InMemoryTenantDomainAuditSink>();
        services.AddSingleton<TenantDomainService>();
    }

    public void Configure(IApplicationBuilder app)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        TenantDomainEndpoints.Map(endpoints);
    }
}
