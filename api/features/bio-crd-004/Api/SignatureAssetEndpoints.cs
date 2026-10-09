using System.Text.Json;
using Biolog.Features.CardObjects.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Biolog.Features.CardObjects.Api;

/// <summary>
/// <c>/api/v1/card-designer/signatures/{lineageId}/versions</c> (ADR-0004 §1.1 versioning).
/// Tenant-scoped (BIO-CRD-004-AC04) - every route resolves the tenant from
/// <see cref="SignatureAssetService"/>'s own <c>ICurrentTenantProvider</c>, never from the URL
/// or a request body, the same fail-closed rule every sibling tenant-plane story applies.
/// </summary>
public static class SignatureAssetEndpoints
{
    private const string BasePath = "/api/v1/card-designer/signatures/{lineageId}/versions";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(BasePath, AppendVersionAsync);
        endpoints.MapGet(BasePath, ListVersionsAsync);
        endpoints.MapGet($"{BasePath}/latest", GetLatestVersionAsync);
        endpoints.MapGet($"{BasePath}/{{version:int}}", GetVersionAsync);
    }

    private static Task AppendVersionAsync(HttpContext context, SignatureAssetService service, string lineageId) => Handle(context, async () =>
    {
        var request = await ReadJsonAsync<AppendSignatureVersionRequest>(context);
        byte[] content;
        try
        {
            content = Convert.FromBase64String(request.ContentBase64);
        }
        catch (FormatException)
        {
            throw new CardObjectsError("content_base64 is not valid base64", "invalid_signature_content", 400);
        }

        if (content.Length == 0)
        {
            throw new CardObjectsError("signature content must not be empty", "invalid_signature_content", 400);
        }

        var asset = service.AppendVersion(lineageId, content);
        return Results.Created($"/api/v1/card-designer/signatures/{lineageId}/versions/{asset.Version}", SignatureAssetEntry.From(asset));
    });

    private static Task ListVersionsAsync(HttpContext context, SignatureAssetService service, string lineageId) => Handle(context, () =>
        Task.FromResult<IResult>(Results.Ok(SignatureAssetVersionsResponse.From(service.ListVersions(lineageId)))));

    private static Task GetLatestVersionAsync(HttpContext context, SignatureAssetService service, string lineageId) => Handle(context, () =>
        Task.FromResult<IResult>(Results.Ok(SignatureAssetEntry.From(service.GetLatestVersion(lineageId)))));

    private static Task GetVersionAsync(HttpContext context, SignatureAssetService service, string lineageId, int version) => Handle(context, () =>
        Task.FromResult<IResult>(Results.Ok(SignatureAssetEntry.From(service.GetVersion(lineageId, version)))));

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

    private static async Task Handle(HttpContext context, Func<Task<IResult>> action)
    {
        try
        {
            var result = await action();
            await result.ExecuteAsync(context);
        }
        catch (CardObjectsError error)
        {
            await ProblemResponse.WriteAsync(context, error);
        }
    }
}
