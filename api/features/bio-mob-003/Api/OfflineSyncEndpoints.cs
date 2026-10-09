using System.Text.Json;
using Biolog.Features.OfflineSync.Audit;
using Biolog.Features.OfflineSync.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Biolog.Features.OfflineSync.Api;

/// <summary>
/// <c>POST /api/v1/offline-mutations</c> (ADR-0004 §1.1 versioning) - BIO-MOB-003's central
/// offline-intake endpoint. The route this story's own mobile-side `HttpSyncTransport` already
/// targets (see its file header for why this exact route is this story's own reasonable choice,
/// not a value read from an approved contract).
///
/// Requires <see cref="IDeviceAuthenticator"/> (device-header credentials, resolving the tenant
/// this request may act on - see <see cref="IDeviceTenantResolutionGateway"/>'s header for why
/// this is never the request body) and <c>Idempotency-Key</c> (ADR-0001 §3.4).
/// </summary>
public static class OfflineSyncEndpoints
{
    public const string IdempotencyKeyHeaderName = "Idempotency-Key";
    public const string IdempotencyReplayedHeaderName = "Idempotency-Replayed";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/v1/offline-mutations", SubmitAsync);
    }

    private static Task SubmitAsync(HttpContext context, OfflineMutationIntakeService service, IDeviceAuthenticator deviceAuthenticator) =>
        Handle(context, async correlationId =>
        {
            var device = await deviceAuthenticator.TryAuthenticateAsync(context.Request, context.RequestAborted)
                ?? throw OfflineSyncError.DeviceAuthenticationFailed();

            var idempotencyKey = context.Request.Headers[IdempotencyKeyHeaderName].ToString();
            if (string.IsNullOrWhiteSpace(idempotencyKey))
            {
                throw OfflineSyncError.IdempotencyKeyRequired();
            }

            var request = await ReadJsonAsync(context);
            var actor = new AuditActor(ActorType.Device, device.DeviceId);

            var (result, replayed) = await service.IntakeAsync(device, request, idempotencyKey, actor, correlationId, context.RequestAborted);

            if (replayed)
            {
                context.Response.Headers[IdempotencyReplayedHeaderName] = "true";
            }

            return Results.Json(OfflineMutationAckResponse.From(result), statusCode: replayed ? 200 : 201);
        });

    private static async Task<OfflineMutationDeltaRequest> ReadJsonAsync(HttpContext context)
    {
        try
        {
            var body = await JsonSerializer.DeserializeAsync<OfflineMutationDeltaRequest>(context.Request.Body, cancellationToken: context.RequestAborted);
            return body ?? throw OfflineSyncError.Validation([new ValidationErrorItem("", "required", "a JSON request body is required")]);
        }
        catch (JsonException ex)
        {
            throw OfflineSyncError.Validation([new ValidationErrorItem("", "invalid_format", $"request body is not valid JSON for this endpoint: {ex.Message}")]);
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
        catch (OfflineSyncError error)
        {
            await ProblemResponse.WriteAsync(context, error, correlationId);
        }
    }
}
