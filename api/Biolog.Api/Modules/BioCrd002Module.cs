using Biolog.Features.CardDesigner.Api;

namespace Biolog.Api.Modules;

/// <summary>
/// WYSIWYG card canvas and physical card formats (BIO-CRD-002), registered the same way every
/// sibling module wraps its feature project - written directly here, in
/// <c>Biolog.Api.Modules</c>, from the start, so the dependency stays one-way.
///
/// Unlike every sibling module, this one registers no services at all: the card-format
/// catalogue and custom-size validator are pure, static reference data and a pure function -
/// no tenant context, no repository, no audit sink, no lock registry. "Dependencies: BIO-CRD-001,
/// BIO-PHO-001" in the story's own metadata is build-wave sequencing, not a runtime dependency -
/// neither brand assets nor photo masters are referenced anywhere in this feature.
/// </summary>
public sealed class BioCrd002Module : IBiologModule
{
    public string Name => "CardDesigner";
    public IReadOnlyCollection<string> DependsOn => [];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void Configure(IApplicationBuilder app)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        CardFormatEndpoints.Map(endpoints);
    }
}
