using Biolog.Api.Modules;

namespace Biolog.Api.Features.BrandAssets;

/// <summary>
/// Wires the versioned tenant brand asset library into the host (BIO-CRD-001), following
/// the module pattern <c>Modules/IBiologModule.cs</c> establishes (BIO-FND-008-AC05).
/// Registered in <c>Program.cs</c>'s module list alongside the others - that list, not
/// this file alone, is "the one place that declares what this host does".
/// </summary>
public sealed class BrandAssetsModule : IBiologModule
{
    public string Name => "BrandAssets";

    // Deliberately does not depend on "Tenant": this feature authenticates its own tenant
    // credential (see ITenantAuthenticator) rather than Tenancy.ITenantContextAccessor,
    // which the Tenant module leaves null on purpose (no real resolution exists yet).
    public IReadOnlyCollection<string> DependsOn => Array.Empty<string>();

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        // Real provisioning path (BIO-CRD-001-QA-02): loads whatever
        // BrandAssets:TenantCredentials:<tenantId> entries configuration exposes -
        // environment variables, user-secrets, or a real secret manager - never a value
        // committed to this repository. See TenantCredentialProvisioning's doc comment.
        services.AddSingleton(_ => TenantCredentialProvisioning.LoadFromConfiguration(configuration));
        services.AddSingleton<ITenantAuthenticator, HeaderTenantAuthenticator>();
        services.AddSingleton<IMalwareScanner, HeuristicMalwareScanner>();
        services.AddSingleton<IBrandAssetRepository, InMemoryBrandAssetRepository>();
        services.AddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();
    }

    public void Configure(IApplicationBuilder app)
    {
        app.Use(async (context, next) =>
        {
            try
            {
                await next();
            }
            catch (BrandAssetApiException error)
            {
                var correlationId = System.Diagnostics.Activity.Current?.Id ?? context.TraceIdentifier;
                var problem = ProblemResponseFactory.From(error, correlationId);
                context.Response.StatusCode = problem.Status;
                context.Response.ContentType = "application/problem+json";
                await context.Response.WriteAsync(
                    System.Text.Json.JsonSerializer.Serialize(problem, BrandAssetJson.Options), context.RequestAborted);
            }
        });
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/brand-assets");
        group.MapPost("/", BrandAssetEndpoints.UploadNewAsync);
        group.MapPost("/{lineageId}/versions", BrandAssetEndpoints.UploadNewVersionAsync);
        group.MapGet("/", BrandAssetEndpoints.List);
        group.MapGet("/{id}", BrandAssetEndpoints.GetById);
        group.MapPost("/{id}/status", BrandAssetEndpoints.UpdateStatusAsync);
    }
}
