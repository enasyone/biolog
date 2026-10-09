using Biolog.Features.TenantRegistry;
using Biolog.Features.TenantRegistry.Api;
using Biolog.Features.TenantRegistry.Audit;
using Biolog.Features.TenantRegistry.Domain;

namespace Biolog.Api.Modules;

/// <summary>
/// The control-plane tenant registry (BIO-TEN-001), registered into the host the same way
/// <see cref="DocumentModule"/> and <see cref="DeviceRegistryModule"/> wrap their feature
/// projects: this thin adapter lives here, in <c>Biolog.Api.Modules</c>, rather than inside
/// <c>api/features/bio-ten-001/**</c>, specifically so the dependency between
/// <c>Biolog.Api</c> and the feature project stays one-way. BIO-TEN-001's own
/// <c>BioTen001Module.cs</c> originally lived inside the feature project and referenced this
/// project back just to see <see cref="IBiologModule"/>, which - combined with
/// <c>Biolog.Api.csproj</c>'s forward <c>ProjectReference</c> to the feature project, added
/// during this integration - created a circular project reference (`dotnet build` hung at
/// "Determining projects to restore..." then failed with no diagnostic). Moved here to
/// restore the one-way shape; the feature project no longer references <c>Biolog.Api</c> at
/// all, matching BIO-DOC-001's and BIO-DEV-001's own module wrappers exactly.
///
/// Depends on nothing - control-plane tenant CRUD needs no tenant context (AC01), so it does
/// not depend on <see cref="TenantModule"/>.
/// </summary>
public sealed class BioTen001Module : IBiologModule
{
    public string Name => "TenantRegistry";
    public IReadOnlyCollection<string> DependsOn => Array.Empty<string>();

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<ITenantRegistry, InMemoryTenantRegistry>();
        services.AddSingleton<ITenantAuditSink, InMemoryTenantAuditSink>();
        services.AddSingleton<TenantService>();
    }

    public void Configure(IApplicationBuilder app)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        TenantEndpoints.Map(endpoints);
    }
}
