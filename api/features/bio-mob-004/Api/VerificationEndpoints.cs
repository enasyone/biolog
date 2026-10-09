using System.Text.Json;
using Biolog.Features.MobileVerification.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Biolog.Features.MobileVerification.Api;

/// <summary>
/// BIO-MOB-004's routes under <c>/api/v1/mobile-verification</c> (ADR-0004 §1.1 versioning):
/// three device-authenticated (the mobile officer's own decision request, history read and
/// own-role read) and one human-facing, tenant/actor/permission-gated (the device-role
/// assignment write - AC03's real authorization decision).
///
/// This story's mapped surface (<c>governance/ui-integration-map.json</c>: <c>surface: "mobile"</c>,
/// <c>routes: ["Flutter application"]</c>, <c>port: "MobileService"</c>) IS the Flutter app itself,
/// the identical shape BIO-MOB-005's own AC05 already established - no demo-bridge pattern is
/// needed. AC05 is satisfied directly: <c>mobile/lib/features/verify/verify_screen.dart</c>
/// (previously a BIO-MOB-001 placeholder) calls these same device-authenticated routes for real.
/// </summary>
public static class VerificationEndpoints
{
    public const string IdempotencyKeyHeaderName = "Idempotency-Key";
    public const string ManageDeviceRolePermissionKey = "mobile_verification.device_role.manage";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/v1/mobile-verification/decisions", RequestDecisionAsync);
        endpoints.MapGet("/api/v1/mobile-verification/decisions", ListHistoryAsync);
        endpoints.MapPost("/api/v1/mobile-verification/decisions/{decisionEventId}/override", OverrideDecisionAsync);
        endpoints.MapGet("/api/v1/mobile-verification/device-role", GetOwnDeviceRoleAsync);
        endpoints.MapPut("/api/v1/mobile-verification/devices/{deviceId}/role", SetDeviceRoleAsync);
    }

    private static Task RequestDecisionAsync(HttpContext context, VerificationService service, IDeviceAuthenticator deviceAuthenticator) =>
        Handle(context, async correlationId =>
        {
            var device = await deviceAuthenticator.TryAuthenticateAsync(context.Request, context.RequestAborted)
                ?? throw VerificationError.DeviceAuthenticationFailed();

            var idempotencyKey = context.Request.Headers[IdempotencyKeyHeaderName].ToString();
            if (string.IsNullOrWhiteSpace(idempotencyKey))
            {
                throw VerificationError.IdempotencyKeyRequired();
            }

            var request = await ReadJsonAsync<RequestVerificationDecisionRequest>(context);
            var actor = new Audit.AuditActor(Audit.ActorType.Device, device.DeviceId);

            var response = await service.RequestDecisionAsync(device, request, idempotencyKey, actor, correlationId, context.RequestAborted);
            return Results.Json(response, statusCode: 201);
        });

    private static Task ListHistoryAsync(HttpContext context, VerificationService service, IDeviceAuthenticator deviceAuthenticator) =>
        Handle(context, async _ =>
        {
            var device = await deviceAuthenticator.TryAuthenticateAsync(context.Request, context.RequestAborted)
                ?? throw VerificationError.DeviceAuthenticationFailed();

            var personId = context.Request.Query["person_id"].ToString();
            if (string.IsNullOrWhiteSpace(personId))
            {
                throw VerificationError.Validation([new ValidationErrorItem("person_id", "required", "person_id query parameter is required")]);
            }

            var response = await service.ListHistoryAsync(device, personId, context.RequestAborted);
            return Results.Json(response);
        });

    private static Task OverrideDecisionAsync(HttpContext context, VerificationService service, IDeviceAuthenticator deviceAuthenticator, string decisionEventId) =>
        Handle(context, async correlationId =>
        {
            var device = await deviceAuthenticator.TryAuthenticateAsync(context.Request, context.RequestAborted)
                ?? throw VerificationError.DeviceAuthenticationFailed();

            var request = await ReadJsonAsync<OverrideDecisionRequest>(context);
            var actor = new Audit.AuditActor(Audit.ActorType.Device, device.DeviceId);

            var response = await service.OverrideDecisionAsync(device, decisionEventId, request, actor, correlationId, context.RequestAborted);
            return Results.Json(response, statusCode: 201);
        });

    private static Task GetOwnDeviceRoleAsync(HttpContext context, VerificationService service, IDeviceAuthenticator deviceAuthenticator) =>
        Handle(context, async _ =>
        {
            var device = await deviceAuthenticator.TryAuthenticateAsync(context.Request, context.RequestAborted)
                ?? throw VerificationError.DeviceAuthenticationFailed();

            var response = await service.GetDeviceRoleAsync(device, context.RequestAborted);
            return Results.Json(response);
        });

    private static Task SetDeviceRoleAsync(
        HttpContext context, VerificationService service, Tenancy.ICurrentTenantProvider tenantProvider,
        IPermissionGateway permissionGateway, ActorProvenanceGate provenanceGate, string deviceId) =>
        Handle(context, async correlationId =>
        {
            var tenantId = tenantProvider.TenantId ?? throw VerificationError.TenantContextMissing();
            var actor = ActorResolver.Resolve(context.Request);
            provenanceGate.Require(context.Request, actor.Id);

            if (!await permissionGateway.ActorHasPermissionAsync(tenantId, actor.Id, ManageDeviceRolePermissionKey, context.RequestAborted))
            {
                throw VerificationError.Forbidden(ManageDeviceRolePermissionKey);
            }

            var request = await ReadJsonAsync<SetDeviceRoleRequest>(context);
            var response = await service.SetDeviceRoleAsync(tenantId, deviceId, request, actor, correlationId, context.RequestAborted);
            return Results.Json(response);
        });

    private static async Task<T> ReadJsonAsync<T>(HttpContext context)
    {
        try
        {
            var body = await JsonSerializer.DeserializeAsync<T>(context.Request.Body, cancellationToken: context.RequestAborted);
            return body ?? throw VerificationError.Validation([new ValidationErrorItem("", "required", "a JSON request body is required")]);
        }
        catch (JsonException ex)
        {
            throw VerificationError.Validation([new ValidationErrorItem("", "invalid_format", $"request body is not valid JSON for this endpoint: {ex.Message}")]);
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
        catch (VerificationError error)
        {
            await ProblemResponse.WriteAsync(context, error, correlationId);
        }
    }
}
