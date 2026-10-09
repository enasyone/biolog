namespace Biolog.Api.Modules;

/// <summary>
/// A self-contained unit of the API host (BIO-FND-008-AC05). Every cross-cutting concern
/// - health, OpenAPI, tenancy, and later observability/auth - registers as one of these
/// instead of being wired directly into <c>Program.cs</c>, so the module list is the one
/// place that declares what the host does and how those pieces depend on each other.
/// </summary>
public interface IBiologModule
{
    /// <summary>Unique, stable name used only to express <see cref="DependsOn"/> edges.</summary>
    string Name { get; }

    /// <summary>Names of other modules that must register/configure before this one.</summary>
    IReadOnlyCollection<string> DependsOn { get; }

    /// <summary>Register this module's services into the DI container.</summary>
    void RegisterServices(IServiceCollection services, IConfiguration configuration);

    /// <summary>Add this module's middleware to the request pipeline, if any.</summary>
    void Configure(IApplicationBuilder app);

    /// <summary>Map this module's routes, if any.</summary>
    void MapEndpoints(IEndpointRouteBuilder endpoints);
}
