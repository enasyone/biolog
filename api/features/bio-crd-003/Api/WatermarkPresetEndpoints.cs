using Biolog.Features.CardTypography.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Biolog.Features.CardTypography.Api;

/// <summary>
/// <c>/api/v1/card-designer/watermark-presets</c> (ADR-0004 §1.1 versioning). Open,
/// non-tenant-scoped reference data - the same shape BIO-CRD-002's own
/// <c>GET /api/v1/card-designer/card-formats</c> and RBAC's own role-catalogue endpoint use.
/// </summary>
public static class WatermarkPresetEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/card-designer/watermark-presets", GetCatalogueAsync);
        endpoints.MapGet("/api/v1/card-designer/watermark-presets/{kind}", GetOneAsync);
    }

    private static Task GetCatalogueAsync(HttpContext context) => Handle(context, _ =>
        Task.FromResult<IResult>(Results.Ok(WatermarkPresetCatalogResponse.Build())));

    private static Task GetOneAsync(HttpContext context, string kind) => Handle(context, _ =>
    {
        if (!WatermarkPresetKindExtensions.TryParse(kind, out var parsed))
        {
            throw CardTypographyError.UnknownPreset(kind);
        }

        var preset = WatermarkPresetCatalog.TryGetPreset(parsed) ?? throw CardTypographyError.UnknownPreset(kind);
        return Task.FromResult<IResult>(Results.Ok(new WatermarkPresetEntry
        {
            Kind = parsed.ToWireString(),
            Text = preset.Text,
            DefaultOpacityPercent = preset.DefaultOpacityPercent,
            DefaultRotationDeg = preset.DefaultRotationDeg,
        }));
    });

    private static async Task Handle(HttpContext context, Func<HttpContext, Task<IResult>> action)
    {
        try
        {
            var result = await action(context);
            await result.ExecuteAsync(context);
        }
        catch (CardTypographyError error)
        {
            await ProblemResponse.WriteAsync(context, error);
        }
    }
}
