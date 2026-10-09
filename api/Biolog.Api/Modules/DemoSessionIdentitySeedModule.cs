using Biolog.Api.Tenancy;

namespace Biolog.Api.Modules;

/// <summary>
/// TD-WEB-PROVENANCE-BRIDGE round 6 - wires <see cref="DemoSessionIdentitySeed"/> into the host.
/// A separate module rather than folded into <see cref="TenantModule"/>: the seed genuinely
/// depends on <c>PersonRegistry</c>'s and <c>Rbac</c>'s own repositories, and
/// <see cref="TenantModule"/> itself has no <see cref="IBiologModule.DependsOn"/> edge (several
/// other modules depend ON it) - adding one here would invert that relationship. No services of
/// its own to register; the seed runs once, in <see cref="Configure"/>, after every module's
/// <see cref="IBiologModule.RegisterServices"/> has already run (<c>Program.cs</c> builds the
/// host before calling any module's <see cref="Configure"/>), so the repositories this seed
/// writes into already exist in the container regardless of module ordering.
///
/// <c>GetAwaiter().GetResult()</c> is safe here, not a deadlock risk: every method
/// <see cref="DemoSessionIdentitySeed.SeedIfConfigured"/> calls
/// (<c>InMemoryPersonRepository</c>/<c>InMemoryPersonRoleAssignmentRepository</c>) returns an
/// already-completed <see cref="Task"/> - there is no real asynchronous I/O to block on, only a
/// signature this story's own repositories share with every sibling repository in the codebase.
/// </summary>
public sealed class DemoSessionIdentitySeedModule : IBiologModule
{
    public string Name => "DemoSessionIdentitySeed";
    public IReadOnlyCollection<string> DependsOn => ["Tenant", "PersonRegistry", "Rbac"];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void Configure(IApplicationBuilder app)
    {
        var configuration = app.ApplicationServices.GetRequiredService<IConfiguration>();
        DemoSessionIdentitySeed.SeedIfConfigured(app.ApplicationServices, configuration).GetAwaiter().GetResult();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
    }
}
