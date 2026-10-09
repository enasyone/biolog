using System.Text.Json;
using Biolog.Features.PhotoBooth.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Biolog.Features.PhotoBooth.Api;

/// <summary>
/// <c>/api/v1/photo-booth/capture-specification</c> and
/// <c>/api/v1/photo-booth/captures/**</c> (ADR-0004 §1.1 versioning). The specification read is
/// open (it is the public contract a booth fetches before it can even attempt a capture); the
/// submit route requires <see cref="IDeviceAuthenticator"/> (BIO-PHO-002-AC02) and
/// <c>Idempotency-Key</c> (BIO-PHO-002-AC03); the approve/reject routes require only an
/// asserted actor (<see cref="ActorResolver"/>) - see that class's header for why.
/// </summary>
public static class CaptureEndpoints
{
    public const string IdempotencyKeyHeaderName = "Idempotency-Key";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/photo-booth/capture-specification", GetSpecificationAsync);

        var captures = endpoints.MapGroup("/api/v1/photo-booth/captures");
        captures.MapPost("", SubmitAsync);
        captures.MapGet("/{personId}", ListForPersonAsync);
        captures.MapGet("/{personId}/{sequenceNumber:int}", GetAsync);
        captures.MapPost("/{personId}/{sequenceNumber:int}/approve", ApproveAsync);
        captures.MapPost("/{personId}/{sequenceNumber:int}/reject", RejectAsync);
    }

    private static Task GetSpecificationAsync(HttpContext context) => Handle(context, _ =>
        Task.FromResult<IResult>(Results.Ok(CaptureSpecificationResponse.From(CaptureSpecification.Current))));

    private static Task SubmitAsync(HttpContext context, CaptureService service, IDeviceAuthenticator deviceAuthenticator) => Handle(context, async correlationId =>
    {
        var deviceId = await deviceAuthenticator.TryAuthenticateAsync(context.Request, context.RequestAborted)
            ?? throw PhotoBoothError.DeviceAuthenticationFailed();

        var idempotencyKey = context.Request.Headers[IdempotencyKeyHeaderName].ToString();
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw PhotoBoothError.IdempotencyKeyRequired();
        }

        if (!context.Request.HasFormContentType)
        {
            throw PhotoBoothError.Validation([new ValidationErrorItem("file", "required", "a multipart/form-data body with a 'file' part is required")]);
        }

        var form = await context.Request.ReadFormAsync(context.RequestAborted);
        var file = form.Files["file"];
        if (file is null || file.Length == 0)
        {
            throw PhotoBoothError.Validation([new ValidationErrorItem("file", "required", "the 'file' part must be present and non-empty")]);
        }

        var personId = form["person_id"].ToString();
        if (string.IsNullOrWhiteSpace(personId))
        {
            throw PhotoBoothError.Validation([new ValidationErrorItem("person_id", "required", "person_id is required")]);
        }

        var faceHeightCompliant = bool.TryParse(form["face_height_compliant"].ToString(), out var faceOk) && faceOk;
        var backgroundCompliant = bool.TryParse(form["background_compliant"].ToString(), out var backgroundOk) && backgroundOk;

        using var stream = file.OpenReadStream();
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, context.RequestAborted);
        var content = buffer.ToArray();

        var actor = new Audit.AuditActor(Audit.ActorType.Device, deviceId);
        var capture = await service.SubmitAsync(
            personId, deviceId, content, faceHeightCompliant, backgroundCompliant, idempotencyKey, actor, correlationId, context.RequestAborted);

        return capture.Status == CaptureStatus.Rejected
            ? Results.UnprocessableEntity(BoothCaptureResponse.From(capture))
            : Results.Created($"/api/v1/photo-booth/captures/{personId}/{capture.SequenceNumber}", BoothCaptureResponse.From(capture));
    });

    private static Task ListForPersonAsync(HttpContext context, CaptureService service, string personId) => Handle(context, async _ =>
    {
        var captures = await service.ListForPersonAsync(personId, context.RequestAborted);
        return Results.Ok(new BoothCaptureListResponse { Data = [.. captures.Select(BoothCaptureResponse.From)] });
    });

    private static Task GetAsync(HttpContext context, CaptureService service, string personId, int sequenceNumber) => Handle(context, async _ =>
    {
        var capture = await service.GetAsync(personId, sequenceNumber, context.RequestAborted)
            ?? throw PhotoBoothError.CaptureNotFound(personId, sequenceNumber);
        return Results.Ok(BoothCaptureResponse.From(capture));
    });

    private static Task ApproveAsync(HttpContext context, CaptureService service, string personId, int sequenceNumber) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        var request = await ReadJsonAsync<ApproveCaptureRequest>(context);
        var capture = await service.ApproveAsync(personId, sequenceNumber, request.ExpectedVersion, actor, correlationId, context.RequestAborted);
        return Results.Ok(BoothCaptureResponse.From(capture));
    });

    private static Task RejectAsync(HttpContext context, CaptureService service, string personId, int sequenceNumber) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        var request = await ReadJsonAsync<RejectCaptureRequest>(context);
        var capture = await service.RejectAsync(personId, sequenceNumber, request.Reason, request.ExpectedVersion, actor, correlationId, context.RequestAborted);
        return Results.Ok(BoothCaptureResponse.From(capture));
    });

    internal static async Task<T> ReadJsonAsync<T>(HttpContext context)
    {
        try
        {
            var body = await JsonSerializer.DeserializeAsync<T>(context.Request.Body, PhotoBoothJsonOptions.Strict, context.RequestAborted);
            if (body is null)
            {
                throw PhotoBoothError.Validation([new ValidationErrorItem("", "required", "a JSON request body is required")]);
            }

            return body;
        }
        catch (JsonException ex)
        {
            throw PhotoBoothError.Validation([new ValidationErrorItem("", "invalid_format", $"request body is not valid JSON for this endpoint: {ex.Message}")]);
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
        catch (PhotoBoothError error)
        {
            await ProblemResponse.WriteAsync(context, error, correlationId);
        }
    }
}
