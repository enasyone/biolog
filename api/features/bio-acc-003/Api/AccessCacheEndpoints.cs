using System.Text.Json;
using Biolog.Features.AccessCache.Demo;
using Biolog.Features.AccessCache.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Biolog.Features.AccessCache.Api;

/// <summary>
/// BIO-ACC-003's routes: three under <c>/api/v1/access-cache</c> (ADR-0004 §1.1 versioning) -
/// two device-authenticated (the mobile client's own revocation-delta pull and presentation-
/// event upload) and one human-facing, tenant/actor/permission-gated read - plus one under
/// <c>/api/v1/access</c>: <see cref="ViewAccessEventsAsync"/>, the exact route
/// <c>web/features/access/live.ts</c>'s already-existing <c>accessViewAccessEventsLivePort</c>
/// (BIO-UX-010 scaffolding, unchanged by this story) already POSTs a bare, headerless request
/// to - it previously had no real endpoint to reach, so it always failed to <c>offline</c> (the
/// documented, correct behaviour for an unconnected LIVE mode). Requiring a tenant/actor on this
/// one route would make it permanently unreachable from the one UI AC06 exists to connect, so it
/// is a dedicated demo bridge (<see cref="AccessCacheDemoBridgeService"/>, mirroring
/// BIO-ATT-001's/BIO-VIS-001's own identical <c>*DemoBridgeService</c> pattern exactly) rather
/// than routed through <see cref="AccessCacheService"/>'s own tenant-scoped surface. This route,
/// plus the matching <c>web/next.config.mjs</c> rewrite, is all that was needed to connect it.
/// </summary>
public static class AccessCacheEndpoints
{
    public const string IdempotencyKeyHeaderName = "Idempotency-Key";
    public const string IdempotencyReplayedHeaderName = "Idempotency-Replayed";
    public const string RequiredPermissionKey = "access_cache.events.read";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/access-cache/revocation-deltas", GetRevocationDeltaAsync);
        endpoints.MapPost("/api/v1/access-cache/presentation-events", SubmitPresentationEventAsync);
        endpoints.MapGet("/api/v1/access-cache/presentation-events", ListPresentationEventsAsync);
        endpoints.MapPost("/api/v1/access/view-access-events", ViewAccessEventsAsync);
    }

    private static Task GetRevocationDeltaAsync(HttpContext context, AccessCacheService service, IDeviceAuthenticator deviceAuthenticator) =>
        Handle(context, async _ =>
        {
            var device = await deviceAuthenticator.TryAuthenticateAsync(context.Request, context.RequestAborted)
                ?? throw AccessCacheError.DeviceAuthenticationFailed();

            var since = context.Request.Query["since"].ToString();
            var response = await service.GetRevocationDeltaAsync(device, string.IsNullOrEmpty(since) ? null : since, context.RequestAborted);
            return Results.Json(response);
        });

    private static Task SubmitPresentationEventAsync(HttpContext context, AccessCacheService service, IDeviceAuthenticator deviceAuthenticator) =>
        Handle(context, async correlationId =>
        {
            var device = await deviceAuthenticator.TryAuthenticateAsync(context.Request, context.RequestAborted)
                ?? throw AccessCacheError.DeviceAuthenticationFailed();

            var idempotencyKey = context.Request.Headers[IdempotencyKeyHeaderName].ToString();
            if (string.IsNullOrWhiteSpace(idempotencyKey))
            {
                throw AccessCacheError.IdempotencyKeyRequired();
            }

            var request = await ReadJsonAsync(context);
            var actor = new Audit.AuditActor(Audit.ActorType.Device, device.DeviceId);

            var (result, replayed) = await service.IntakePresentationEventAsync(device, request, idempotencyKey, actor, correlationId, context.RequestAborted);

            if (replayed)
            {
                context.Response.Headers[IdempotencyReplayedHeaderName] = "true";
            }

            return Results.Json(result, statusCode: replayed ? 200 : 201);
        });

    private static Task ListPresentationEventsAsync(
        HttpContext context, AccessCacheService service, Tenancy.ICurrentTenantProvider tenantProvider,
        IPermissionGateway permissionGateway, ActorProvenanceGate provenanceGate) =>
        Handle(context, async _ =>
        {
            var tenantId = tenantProvider.TenantId ?? throw AccessCacheError.TenantContextMissing();
            var actor = ActorResolver.Resolve(context.Request);
            provenanceGate.Require(context.Request, actor.Id);

            if (!await permissionGateway.ActorHasPermissionAsync(tenantId, actor.Id, RequiredPermissionKey, context.RequestAborted))
            {
                throw AccessCacheError.Forbidden(RequiredPermissionKey);
            }

            var limitRaw = context.Request.Query["limit"].ToString();
            var limit = int.TryParse(limitRaw, out var parsed) && parsed > 0 ? Math.Min(parsed, 200) : 50;

            var response = await service.ListRecentPresentationEventsAsync(tenantId, limit, context.RequestAborted);
            return Results.Json(response);
        });

    private static Task ViewAccessEventsAsync(HttpContext context, AccessCacheDemoBridgeService bridge) =>
        Handle(context, async _ =>
        {
            var message = await bridge.ViewAccessEventsAsync(context.RequestAborted);
            return Results.Json(new { message });
        });

    private static async Task<PresentationEventIntakeRequest> ReadJsonAsync(HttpContext context)
    {
        try
        {
            var body = await JsonSerializer.DeserializeAsync<PresentationEventIntakeRequest>(context.Request.Body, cancellationToken: context.RequestAborted);
            return body ?? throw AccessCacheError.Validation([new ValidationErrorItem("", "required", "a JSON request body is required")]);
        }
        catch (JsonException ex)
        {
            throw AccessCacheError.Validation([new ValidationErrorItem("", "invalid_format", $"request body is not valid JSON for this endpoint: {ex.Message}")]);
        }
    }

    private static async Task Handle(HttpContext context, Func<string, Task<IResult>> action)
    {
        var correlationId = CorrelationIdHeader.Resolve(context.Request);
        context.Response.Headers[CorrelationIdHeader.HeaderName] = correlationId;

        try
        {
            var result = await action(correlationId);
            await result.ExecuteAsync(context);
        }
        catch (AccessCacheError error)
        {
            await ProblemResponse.WriteAsync(context, error, correlationId);
        }
    }
}
