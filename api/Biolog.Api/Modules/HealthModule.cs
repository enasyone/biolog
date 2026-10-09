using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace Biolog.Api.Modules;

/// <summary>
/// Health/readiness endpoints (BIO-FND-008-AC02). Liveness answers "is the process up" and
/// never runs a dependency check, so an orchestrator never restarts a healthy process just
/// because a downstream dependency is slow. Readiness runs every registered health check -
/// none yet, since this shell has no dependencies of its own - and is the extension point
/// later stories add real checks (database, cache, message broker) to.
/// </summary>
public sealed class HealthModule : IBiologModule
{
    public string Name => "Health";
    public IReadOnlyCollection<string> DependsOn => Array.Empty<string>();

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddHealthChecks();
    }

    public void Configure(IApplicationBuilder app)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false,
        });
        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = _ => true,
        });
    }
}
