using Biolog.Features.CardTypography.Api;

namespace Biolog.Api.Modules;

/// <summary>
/// Card typography, name fields and watermarks (BIO-CRD-003), registered the same way every
/// sibling module wraps its feature project - written directly here, in
/// <c>Biolog.Api.Modules</c>, from the start, so the dependency stays one-way.
///
/// Like <see cref="BioCrd002Module"/>, this registers no services at all: the watermark preset
/// catalogue is pure, static reference data. "Dependencies: BIO-CRD-002" in the story's own
/// metadata is build-wave sequencing (both stories share the `card-designer` conflict group),
/// not a runtime dependency - BIO-CRD-002's own types are never referenced anywhere in this
/// backend feature (the frontend composes with BIO-CRD-002's exported TypeScript, a different
/// language boundary with no equivalent to this project's own one-way C# dependency rule).
/// </summary>
public sealed class BioCrd003Module : IBiologModule
{
    public string Name => "CardTypography";
    public IReadOnlyCollection<string> DependsOn => [];

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void Configure(IApplicationBuilder app)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        WatermarkPresetEndpoints.Map(endpoints);
    }
}
