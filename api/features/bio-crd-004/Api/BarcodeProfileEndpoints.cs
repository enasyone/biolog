using System.Text.Json;
using Biolog.Features.CardObjects.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Biolog.Features.CardObjects.Api;

/// <summary>
/// <c>/api/v1/card-designer/barcode-profiles</c> and <c>/api/v1/card-designer/barcode/validate</c>
/// (ADR-0004 §1.1 versioning). Both are open, non-tenant-scoped - the same shape BIO-CRD-002's
/// own card-format catalogue/validator endpoints use - since neither response depends on who is
/// asking or which tenant they belong to.
/// </summary>
public static class BarcodeProfileEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/card-designer/barcode-profiles", GetCatalogueAsync);
        endpoints.MapPost("/api/v1/card-designer/barcode/validate", ValidateAsync);
    }

    private static Task GetCatalogueAsync(HttpContext context) => Handle(context, _ =>
        Task.FromResult<IResult>(Results.Ok(BarcodeProfileCatalogResponse.Build())));

    private static Task ValidateAsync(HttpContext context) => Handle(context, async _ =>
    {
        var request = await ReadJsonAsync<ValidateBarcodeRequest>(context);

        if (!BarcodeProfileExtensions.TryParse(request.Profile, out var profile))
        {
            throw CardObjectsError.UnknownBarcodeProfile(request.Profile);
        }

        var result = BarcodeValidation.Validate(new BarcodeValidationRequest(
            profile, request.QuietZoneMm, request.WidthMm, request.HeightMm, request.ForegroundColorHex, request.BackgroundColorHex));
        return Results.Ok(ValidateBarcodeResponse.From(result));
    });

    private static async Task<T> ReadJsonAsync<T>(HttpContext context)
    {
        try
        {
            var body = await JsonSerializer.DeserializeAsync<T>(context.Request.Body, ValidateBarcodeRequestJson.Options, context.RequestAborted);
            if (body is null)
            {
                throw new CardObjectsError("a JSON request body is required", "invalid_json_body", 400);
            }

            return body;
        }
        catch (JsonException ex)
        {
            throw new CardObjectsError(ex.Message, "invalid_json_body", 400);
        }
    }

    private static async Task Handle(HttpContext context, Func<HttpContext, Task<IResult>> action)
    {
        try
        {
            var result = await action(context);
            await result.ExecuteAsync(context);
        }
        catch (CardObjectsError error)
        {
            await ProblemResponse.WriteAsync(context, error);
        }
    }
}
