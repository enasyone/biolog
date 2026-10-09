using System.Text.Json;
using Biolog.Features.CardDesigner.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Biolog.Features.CardDesigner.Api;

/// <summary>
/// <c>/api/v1/card-designer/card-formats</c> and <c>/api/v1/card-designer/custom-size/validate</c>
/// (ADR-0004 §1.1 versioning). Both are open, non-tenant-scoped reads of stateless reference
/// data - the same shape RBAC's own <c>GET /api/v1/roles</c> catalogue endpoint uses (BIO-IDN-006)
/// - not gated behind tenant or actor resolution, since neither response depends on who is
/// asking or which tenant they belong to.
/// </summary>
public static class CardFormatEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/card-designer/card-formats", GetCatalogueAsync);
        endpoints.MapPost("/api/v1/card-designer/custom-size/validate", ValidateCustomSizeAsync);
    }

    private static Task GetCatalogueAsync(HttpContext context) => Handle(context, _ =>
        Task.FromResult<IResult>(Results.Ok(CardFormatCatalogResponse.Build())));

    private static Task ValidateCustomSizeAsync(HttpContext context) => Handle(context, async _ =>
    {
        var request = await ReadJsonAsync<ValidateCustomSizeRequest>(context);
        var result = CustomCardSizeValidator.Validate(request.WidthMm, request.HeightMm);
        return Results.Ok(ValidateCustomSizeResponse.From(result));
    });

    private static async Task<T> ReadJsonAsync<T>(HttpContext context)
    {
        try
        {
            var body = await JsonSerializer.DeserializeAsync<T>(
                context.Request.Body,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower },
                context.RequestAborted);
            if (body is null)
            {
                throw CardDesignerError.InvalidJsonBody("a JSON request body is required");
            }

            return body;
        }
        catch (JsonException ex)
        {
            throw CardDesignerError.InvalidJsonBody(ex.Message);
        }
    }

    private static async Task Handle(HttpContext context, Func<HttpContext, Task<IResult>> action)
    {
        try
        {
            var result = await action(context);
            await result.ExecuteAsync(context);
        }
        catch (CardDesignerError error)
        {
            await ProblemResponse.WriteAsync(context, error);
        }
    }
}
