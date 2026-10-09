using System.Text.Json;
using Biolog.Features.OfflineEnrolment.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Biolog.Features.OfflineEnrolment.Api;

/// <summary>
/// BIO-MOB-005's routes under <c>/api/v1/enrolment</c> (ADR-0004 §1.1 versioning): two
/// device-authenticated (the mobile app's own capture-policy pull and package submission) and
/// three human-facing, tenant/actor/permission-gated (capture-policy admin write, package
/// listing, duplicate-candidate listing and decision - AC04's human resolution queue).
///
/// Unlike BIO-ACC-003-AC06, this story's mapped surface (<c>governance/ui-integration-map.json</c>:
/// <c>surface: "mobile"</c>, <c>routes: ["Flutter application"]</c>, <c>port: "MobileService"</c>)
/// IS the Flutter app itself, not a web page - there is no generic headerless
/// <c>createLiveAdapter</c> caller to bridge here, so no demo-bridge pattern is needed. AC05 is
/// satisfied directly: <c>mobile/lib/features/enrolment/enrolment_screen.dart</c> (previously a
/// placeholder shell) now calls these same device-authenticated routes for real.
/// </summary>
public static class EnrolmentEndpoints
{
    public const string IdempotencyKeyHeaderName = "Idempotency-Key";
    public const string IdempotencyReplayedHeaderName = "Idempotency-Replayed";
    public const string ManagePolicyPermissionKey = "offline_enrolment.capture_policy.manage";
    public const string ReadPackagesPermissionKey = "offline_enrolment.packages.read";
    public const string ReviewDuplicatesPermissionKey = "offline_enrolment.duplicate_candidates.review";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/enrolment/capture-policy", GetCapturePolicyAsync);
        endpoints.MapPut("/api/v1/enrolment/capture-policy", SetCapturePolicyAsync);
        endpoints.MapPost("/api/v1/enrolment/packages", SubmitPackageAsync);
        endpoints.MapGet("/api/v1/enrolment/packages", ListPackagesAsync);
        endpoints.MapGet("/api/v1/enrolment/duplicate-candidates", ListDuplicateCandidatesAsync);
        endpoints.MapPost("/api/v1/enrolment/duplicate-candidates/{candidateId}/decisions", DecideDuplicateCandidateAsync);
    }

    private static Task GetCapturePolicyAsync(HttpContext context, EnrolmentPackageService service, IDeviceAuthenticator deviceAuthenticator) =>
        Handle(context, async _ =>
        {
            var device = await deviceAuthenticator.TryAuthenticateAsync(context.Request, context.RequestAborted)
                ?? throw EnrolmentError.DeviceAuthenticationFailed();
            var response = await service.GetCapturePolicyAsync(device, context.RequestAborted);
            return Results.Json(response);
        });

    private static Task SetCapturePolicyAsync(
        HttpContext context, EnrolmentPackageService service, Tenancy.ICurrentTenantProvider tenantProvider,
        IPermissionGateway permissionGateway, ActorProvenanceGate provenanceGate) =>
        Handle(context, async correlationId =>
        {
            var tenantId = tenantProvider.TenantId ?? throw EnrolmentError.TenantContextMissing();
            var actor = ActorResolver.Resolve(context.Request);
            provenanceGate.Require(context.Request, actor.Id);

            if (!await permissionGateway.ActorHasPermissionAsync(tenantId, actor.Id, ManagePolicyPermissionKey, context.RequestAborted))
            {
                throw EnrolmentError.Forbidden(ManagePolicyPermissionKey);
            }

            var request = await ReadJsonAsync<SetCapturePolicyRequest>(context);
            var response = await service.SetCapturePolicyAsync(tenantId, request, actor, correlationId, context.RequestAborted);
            return Results.Json(response);
        });

    private static Task SubmitPackageAsync(HttpContext context, EnrolmentPackageService service, IDeviceAuthenticator deviceAuthenticator) =>
        Handle(context, async correlationId =>
        {
            var device = await deviceAuthenticator.TryAuthenticateAsync(context.Request, context.RequestAborted)
                ?? throw EnrolmentError.DeviceAuthenticationFailed();

            var idempotencyKey = context.Request.Headers[IdempotencyKeyHeaderName].ToString();
            if (string.IsNullOrWhiteSpace(idempotencyKey))
            {
                throw EnrolmentError.IdempotencyKeyRequired();
            }

            var request = await ReadJsonAsync<EnrolmentPackageSubmitRequest>(context);
            var actor = new Audit.AuditActor(Audit.ActorType.Device, device.DeviceId);

            var (result, replayed) = await service.SubmitPackageAsync(device, request, idempotencyKey, actor, correlationId, context.RequestAborted);

            if (replayed)
            {
                context.Response.Headers[IdempotencyReplayedHeaderName] = "true";
            }

            return Results.Json(result, statusCode: replayed ? 200 : 201);
        });

    private static Task ListPackagesAsync(
        HttpContext context, EnrolmentPackageService service, Tenancy.ICurrentTenantProvider tenantProvider,
        IPermissionGateway permissionGateway, ActorProvenanceGate provenanceGate) =>
        Handle(context, async _ =>
        {
            var tenantId = tenantProvider.TenantId ?? throw EnrolmentError.TenantContextMissing();
            var actor = ActorResolver.Resolve(context.Request);
            provenanceGate.Require(context.Request, actor.Id);

            if (!await permissionGateway.ActorHasPermissionAsync(tenantId, actor.Id, ReadPackagesPermissionKey, context.RequestAborted))
            {
                throw EnrolmentError.Forbidden(ReadPackagesPermissionKey);
            }

            var statusRaw = context.Request.Query["status"].ToString();
            if (!TryParseStatus(statusRaw, out var status))
            {
                throw EnrolmentError.Validation([new ValidationErrorItem("status", "invalid_value", "must be one of SUBMITTED, PENDING_REVIEW, ACCEPTED, REJECTED_DUPLICATE")]);
            }

            var response = await service.ListPackagesByStatusAsync(tenantId, status, context.RequestAborted);
            return Results.Json(response);
        });

    private static Task ListDuplicateCandidatesAsync(
        HttpContext context, EnrolmentPackageService service, Tenancy.ICurrentTenantProvider tenantProvider,
        IPermissionGateway permissionGateway, ActorProvenanceGate provenanceGate) =>
        Handle(context, async _ =>
        {
            var tenantId = tenantProvider.TenantId ?? throw EnrolmentError.TenantContextMissing();
            var actor = ActorResolver.Resolve(context.Request);
            provenanceGate.Require(context.Request, actor.Id);

            if (!await permissionGateway.ActorHasPermissionAsync(tenantId, actor.Id, ReviewDuplicatesPermissionKey, context.RequestAborted))
            {
                throw EnrolmentError.Forbidden(ReviewDuplicatesPermissionKey);
            }

            var response = await service.ListPendingDuplicateCandidatesAsync(tenantId, context.RequestAborted);
            return Results.Json(response);
        });

    private static Task DecideDuplicateCandidateAsync(
        HttpContext context, EnrolmentPackageService service, Tenancy.ICurrentTenantProvider tenantProvider,
        IPermissionGateway permissionGateway, ActorProvenanceGate provenanceGate, string candidateId) =>
        Handle(context, async correlationId =>
        {
            var tenantId = tenantProvider.TenantId ?? throw EnrolmentError.TenantContextMissing();
            var actor = ActorResolver.Resolve(context.Request);
            provenanceGate.Require(context.Request, actor.Id);

            if (!await permissionGateway.ActorHasPermissionAsync(tenantId, actor.Id, ReviewDuplicatesPermissionKey, context.RequestAborted))
            {
                throw EnrolmentError.Forbidden(ReviewDuplicatesPermissionKey);
            }

            var request = await ReadJsonAsync<DuplicateCandidateDecisionRequest>(context);
            var response = await service.DecideDuplicateCandidateAsync(tenantId, candidateId, request, actor, correlationId, context.RequestAborted);
            return Results.Json(response);
        });

    private static bool TryParseStatus(string? value, out EnrolmentPackageStatus status)
    {
        switch (value)
        {
            case "SUBMITTED": status = EnrolmentPackageStatus.Submitted; return true;
            case "PENDING_REVIEW": status = EnrolmentPackageStatus.PendingReview; return true;
            case "ACCEPTED": status = EnrolmentPackageStatus.Accepted; return true;
            case "REJECTED_DUPLICATE": status = EnrolmentPackageStatus.RejectedDuplicate; return true;
            default: status = default; return false;
        }
    }

    private static async Task<T> ReadJsonAsync<T>(HttpContext context)
    {
        try
        {
            var body = await JsonSerializer.DeserializeAsync<T>(context.Request.Body, cancellationToken: context.RequestAborted);
            return body ?? throw EnrolmentError.Validation([new ValidationErrorItem("", "required", "a JSON request body is required")]);
        }
        catch (JsonException ex)
        {
            throw EnrolmentError.Validation([new ValidationErrorItem("", "invalid_format", $"request body is not valid JSON for this endpoint: {ex.Message}")]);
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
        catch (EnrolmentError error)
        {
            await ProblemResponse.WriteAsync(context, error, correlationId);
        }
    }
}
