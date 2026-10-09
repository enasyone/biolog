using System.Text.Json;
using Biolog.Features.Photography.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Biolog.Features.Photography.Api;

/// <summary>
/// <c>/api/v1/people/{personId}/photo-masters</c>,
/// <c>/api/v1/people/{personId}/photo-masters/{version}/derivatives/{kind}</c> and
/// <c>/api/v1/people/{personId}/photo-assignment</c> (ADR-0004 §1.1 versioning), nested under
/// the owning person, matching BIO-IDN-002/003/006's convention. Every mutating route requires
/// an asserted actor (<see cref="ActorResolver"/>) for audit attribution only - see that
/// class's header for why no provenance gate applies here.
/// </summary>
public static class PhotoEndpoints
{
    private const string PeopleBasePath = "/api/v1/people/{personId}";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var masters = endpoints.MapGroup($"{PeopleBasePath}/photo-masters");
        masters.MapPost("", AppendMasterAsync);
        masters.MapGet("", ListMastersAsync);
        masters.MapGet("/{version:int}", GetMasterAsync);

        var derivatives = endpoints.MapGroup($"{PeopleBasePath}/photo-masters/{{version:int}}/derivatives");
        derivatives.MapPost("/{kind}/regenerate", RegenerateDerivativeAsync);
        derivatives.MapGet("/{kind}", GetDerivativeAsync);
        derivatives.MapGet("", ListDerivativesForMasterAsync);

        var assignment = endpoints.MapGroup($"{PeopleBasePath}/photo-assignment");
        assignment.MapGet("", GetAssignmentAsync);
        assignment.MapPut("", AssignAsync);
    }

    private static Task AppendMasterAsync(HttpContext context, PhotoMasterService service, string personId) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        var content = await ReadUploadedFileAsync(context);
        var master = await service.AppendAsync(personId, content, actor, correlationId, context.RequestAborted);
        return Results.Created($"/api/v1/people/{personId}/photo-masters/{master.Version}", PhotoMasterResponse.From(master));
    });

    private static Task ListMastersAsync(HttpContext context, PhotoMasterService service, string personId) => Handle(context, async _ =>
    {
        var masters = await service.ListVersionsAsync(personId, context.RequestAborted);
        return Results.Ok(new PhotoMasterListResponse { Data = [.. masters.Select(PhotoMasterResponse.From)] });
    });

    private static Task GetMasterAsync(HttpContext context, PhotoMasterService service, string personId, int version) => Handle(context, async _ =>
    {
        var master = await service.GetVersionAsync(personId, version, context.RequestAborted)
            ?? throw PhotoError.MasterNotFound(personId, version);
        return Results.Ok(PhotoMasterResponse.From(master));
    });

    private static Task RegenerateDerivativeAsync(HttpContext context, PhotoDerivativeService service, string personId, int version, string kind) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        var parsedKind = ParseKindOrThrow(kind);
        var derivative = await service.RegenerateAsync(personId, version, parsedKind, actor, correlationId, context.RequestAborted);
        return Results.Ok(PhotoDerivativeResponse.From(derivative));
    });

    private static Task GetDerivativeAsync(HttpContext context, PhotoDerivativeService service, string personId, int version, string kind) => Handle(context, async _ =>
    {
        var parsedKind = ParseKindOrThrow(kind);
        var derivative = await service.GetAsync(personId, version, parsedKind, context.RequestAborted)
            ?? throw PhotoError.DerivativeNotFound(personId, version, parsedKind);
        return Results.Ok(PhotoDerivativeResponse.From(derivative));
    });

    private static Task ListDerivativesForMasterAsync(HttpContext context, PhotoDerivativeService service, string personId, int version) => Handle(context, async _ =>
    {
        var derivatives = await service.ListForMasterAsync(personId, version, context.RequestAborted);
        return Results.Ok(new PhotoDerivativeListResponse { Data = [.. derivatives.Select(PhotoDerivativeResponse.From)] });
    });

    private static Task GetAssignmentAsync(HttpContext context, PhotoAssignmentService service, string personId) => Handle(context, async _ =>
    {
        var assignment = await service.GetAsync(personId, context.RequestAborted);
        return assignment is null ? Results.NotFound() : Results.Ok(PhotoAssignmentResponse.From(assignment));
    });

    private static Task AssignAsync(HttpContext context, PhotoAssignmentService service, string personId) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        var request = await ReadJsonAsync<AssignPhotoRequest>(context);
        var assignment = await service.AssignAsync(personId, request.MasterVersion, request.ExpectedVersion, actor, correlationId, context.RequestAborted);
        return Results.Ok(PhotoAssignmentResponse.From(assignment));
    });

    private static DerivativeKind ParseKindOrThrow(string kind) =>
        DerivativeKindExtensions.TryParse(kind, out var parsed)
            ? parsed
            : throw PhotoError.Validation([new ValidationErrorItem("kind", "invalid_format", "kind must be one of: print, display")]);

    private static async Task<byte[]> ReadUploadedFileAsync(HttpContext context)
    {
        if (!context.Request.HasFormContentType)
        {
            throw PhotoError.Validation([new ValidationErrorItem("file", "required", "a multipart/form-data body with a 'file' part is required")]);
        }

        var form = await context.Request.ReadFormAsync(context.RequestAborted);
        var file = form.Files["file"];
        if (file is null || file.Length == 0)
        {
            throw PhotoError.Validation([new ValidationErrorItem("file", "required", "the 'file' part must be present and non-empty")]);
        }

        using var stream = file.OpenReadStream();
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, context.RequestAborted);
        return buffer.ToArray();
    }

    internal static async Task<T> ReadJsonAsync<T>(HttpContext context)
    {
        try
        {
            var body = await JsonSerializer.DeserializeAsync<T>(context.Request.Body, PhotoJsonOptions.Strict, context.RequestAborted);
            if (body is null)
            {
                throw PhotoError.Validation([new ValidationErrorItem("", "required", "a JSON request body is required")]);
            }

            return body;
        }
        catch (JsonException ex)
        {
            throw PhotoError.Validation([new ValidationErrorItem("", "invalid_format", $"request body is not valid JSON for this endpoint: {ex.Message}")]);
        }
    }

    internal static async Task Handle(HttpContext context, Func<string, Task<IResult>> action)
    {
        var correlationId = CorrelationIdHeader.Resolve(context.Request);
        context.Response.Headers[CorrelationIdHeader.HeaderName] = correlationId;

        try
        {
            var result = await action(correlationId);
            await result.ExecuteAsync(context);
        }
        catch (PhotoError error)
        {
            await ProblemResponse.WriteAsync(context, error, correlationId);
        }
    }
}
