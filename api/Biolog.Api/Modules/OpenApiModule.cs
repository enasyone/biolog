namespace Biolog.Api.Modules;

/// <summary>
/// OpenAPI document generation (BIO-FND-008-AC03), via the framework's native
/// <c>Microsoft.AspNetCore.OpenApi</c> generator rather than a third-party dependency.
/// Serves the generated document at <c>/openapi/v1.json</c>.
/// </summary>
public sealed class OpenApiModule : IBiologModule
{
    public string Name => "OpenApi";
    public IReadOnlyCollection<string> DependsOn => Array.Empty<string>();

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOpenApi();
    }

    public void Configure(IApplicationBuilder app)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapOpenApi();
    }
}
