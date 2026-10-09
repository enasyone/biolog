using Biolog.Api.Modules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Biolog.UnitTests.Modules;

/// <summary>Test double for <see cref="IBiologModule"/>: records its name to a shared log
/// when registered, so ordering can be asserted without any real ASP.NET Core wiring.</summary>
internal sealed class FakeModule : IBiologModule
{
    private readonly List<string>? _log;

    public FakeModule(string name, IReadOnlyCollection<string>? dependsOn = null, List<string>? log = null)
    {
        Name = name;
        DependsOn = dependsOn ?? Array.Empty<string>();
        _log = log;
    }

    public string Name { get; }
    public IReadOnlyCollection<string> DependsOn { get; }

    public void RegisterServices(IServiceCollection services, IConfiguration configuration) => _log?.Add(Name);
    public void Configure(IApplicationBuilder app)
    {
    }
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
    }
}
