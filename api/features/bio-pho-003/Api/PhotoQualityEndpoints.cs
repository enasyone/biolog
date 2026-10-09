using System.Text.Json;
using Biolog.Features.PhotoQuality.Domain;
using Biolog.Features.PhotoQuality.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Biolog.Features.PhotoQuality.Api;

/// <summary>
/// <c>/api/v1/photo-quality/captures/**</c> (ADR-0004 §1.1 versioning). Every route - reads
/// included - requires a provenance-verified actor (<see cref="ActorResolver"/> +
/// <see cref="ActorProvenanceGate"/>) holding a real, tenant-scoped grant for that route's own
/// <see cref="PhotoQualityPermissions"/> key (<see cref="AuthorizeAsync"/>). ZB-PHO003-01
/// (Zainab, security round 1, Critical): round 1 gated nothing at all - see
/// <see cref="ActorResolver"/>'s own header for the full account of why that was wrong.
/// </summary>
public static class PhotoQualityEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var captures = endpoints.MapGroup("/api/v1/photo-quality/captures");
        captures.MapPost("", SubmitAsync);
        captures.MapGet("/{personId}", ListForPersonAsync);
        captures.MapGet("/{personId}/{sequenceNumber:int}", GetAsync);
        captures.MapPost("/{personId}/{sequenceNumber:int}/approve", ApproveAsync);
        captures.MapPost("/{personId}/{sequenceNumber:int}/reject", RejectAsync);
    }

    private static Task SubmitAsync(
        HttpContext context, PhotoQualityService service, ICurrentTenantProvider tenantProvider,
        ActorProvenanceGate provenanceGate, IPermissionGateway permissionGateway) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        await AuthorizeAsync(context.Request, actor.Id, RequireTenant(tenantProvider), PhotoQualityPermissions.Submit, provenanceGate, permissionGateway, context.RequestAborted);

        if (!context.Request.HasFormContentType)
        {
            throw PhotoQualityError.Validation([new ValidationErrorItem("file", "required", "a multipart/form-data body with a 'file' part is required")]);
        }

        var form = await context.Request.ReadFormAsync(context.RequestAborted);
        var file = form.Files["file"];
        if (file is null || file.Length == 0)
        {
            throw PhotoQualityError.Validation([new ValidationErrorItem("file", "required", "the 'file' part must be present and non-empty")]);
        }

        var personId = form["person_id"].ToString();
        if (string.IsNullOrWhiteSpace(personId))
        {
            throw PhotoQualityError.Validation([new ValidationErrorItem("person_id", "required", "person_id is required")]);
        }

        using var stream = file.OpenReadStream();
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, context.RequestAborted);
        var content = buffer.ToArray();

        var capture = await service.SubmitAsync(personId, content, actor, correlationId, context.RequestAborted);
        return Results.Created($"/api/v1/photo-quality/captures/{personId}/{capture.SequenceNumber}", PhotoQualityCaptureResponse.From(capture));
    });

    private static Task ListForPersonAsync(
        HttpContext context, PhotoQualityService service, ICurrentTenantProvider tenantProvider,
        ActorProvenanceGate provenanceGate, IPermissionGateway permissionGateway, string personId) => Handle(context, async _ =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        await AuthorizeAsync(context.Request, actor.Id, RequireTenant(tenantProvider), PhotoQualityPermissions.View, provenanceGate, permissionGateway, context.RequestAborted);

        var captures = await service.ListForPersonAsync(personId, context.RequestAborted);
        return Results.Ok(new PhotoQualityCaptureListResponse { Data = [.. captures.Select(PhotoQualityCaptureResponse.From)] });
    });

    private static Task GetAsync(
        HttpContext context, PhotoQualityService service, ICurrentTenantProvider tenantProvider,
        ActorProvenanceGate provenanceGate, IPermissionGateway permissionGateway, string personId, int sequenceNumber) => Handle(context, async _ =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        await AuthorizeAsync(context.Request, actor.Id, RequireTenant(tenantProvider), PhotoQualityPermissions.View, provenanceGate, permissionGateway, context.RequestAborted);

        var capture = await service.GetAsync(personId, sequenceNumber, context.RequestAborted)
            ?? throw PhotoQualityError.CaptureNotFound(personId, sequenceNumber);
        return Results.Ok(PhotoQualityCaptureResponse.From(capture));
    });

    private static Task ApproveAsync(
        HttpContext context, PhotoQualityService service, ICurrentTenantProvider tenantProvider,
        ActorProvenanceGate provenanceGate, IPermissionGateway permissionGateway, string personId, int sequenceNumber) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        await AuthorizeAsync(context.Request, actor.Id, RequireTenant(tenantProvider), PhotoQualityPermissions.Decide, provenanceGate, permissionGateway, context.RequestAborted);

        var request = await ReadJsonAsync<ApproveCaptureRequest>(context);
        var capture = await service.ApproveAsync(personId, sequenceNumber, request.ExpectedVersion, actor, correlationId, context.RequestAborted);
        return Results.Ok(PhotoQualityCaptureResponse.From(capture));
    });

    private static Task RejectAsync(
        HttpContext context, PhotoQualityService service, ICurrentTenantProvider tenantProvider,
        ActorProvenanceGate provenanceGate, IPermissionGateway permissionGateway, string personId, int sequenceNumber) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        await AuthorizeAsync(context.Request, actor.Id, RequireTenant(tenantProvider), PhotoQualityPermissions.Decide, provenanceGate, permissionGateway, context.RequestAborted);

        var request = await ReadJsonAsync<RejectCaptureRequest>(context);
        var capture = await service.RejectAsync(personId, sequenceNumber, request.Reason, request.ExpectedVersion, actor, correlationId, context.RequestAborted);
        return Results.Ok(PhotoQualityCaptureResponse.From(capture));
    });

    private static string RequireTenant(ICurrentTenantProvider tenantProvider) =>
        tenantProvider.TenantId ?? throw PhotoQualityError.TenantContextMissing();

    /// <summary>ADR-0006 Decision 3: verify the asserted actor's provenance proof BEFORE
    /// consulting the real RBAC grant lookup - never the other way round, and never skipping
    /// either. Shared by every route this feature maps, reads included (ZB-PHO003-01).</summary>
    private static async Task AuthorizeAsync(
        HttpRequest request, string actorId, string tenantId, string requiredPermission,
        ActorProvenanceGate provenanceGate, IPermissionGateway permissionGateway, CancellationToken cancellationToken)
    {
        provenanceGate.Require(request, actorId);

        if (!await permissionGateway.ActorHasPermissionAsync(tenantId, actorId, requiredPermission, cancellationToken))
        {
            throw PhotoQualityError.PermissionRequired(requiredPermission);
        }
    }

    internal static async Task<T> ReadJsonAsync<T>(HttpContext context)
    {
        try
        {
            var body = await JsonSerializer.DeserializeAsync<T>(context.Request.Body, PhotoQualityJsonOptions.Strict, context.RequestAborted);
            if (body is null)
            {
                throw PhotoQualityError.Validation([new ValidationErrorItem("", "required", "a JSON request body is required")]);
            }

            return body;
        }
        catch (JsonException ex)
        {
            throw PhotoQualityError.Validation([new ValidationErrorItem("", "invalid_format", $"request body is not valid JSON for this endpoint: {ex.Message}")]);
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
        catch (PhotoQualityError error)
        {
            await ProblemResponse.WriteAsync(context, error, correlationId);
        }
    }
}
