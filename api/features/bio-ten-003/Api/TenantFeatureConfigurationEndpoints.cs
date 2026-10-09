using System.Text.Json;
using Biolog.Features.TenantFeatureConfig.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Biolog.Features.TenantFeatureConfig.Api;

/// <summary>
/// BIO-TEN-003-AC05: the real HTTP surface behind <c>/administration/tenants</c>'s feature
/// module toggles. Tenant-plane scoped - the route carries the tenant id explicitly in the
/// path, unlike BIO-TEN-001's own control-plane-scoped <c>/control-plane/tenants</c> routes
/// (a BTS admin console reads/writes several tenants' configuration from one surface, so an
/// ambient-single-tenant binding would not fit - Zainab's round-1 review named this an
/// acceptable alternative to ambient binding, conditioned on real provenance/permission/
/// existence checks, which round 2 adds below). Versioned under <c>/api/v1</c> per ADR-0004
/// Decision 1.1.
///
/// <b>Round-2 remediation (ZB-TEN003-01/BIO-TEN-003-F01, Critical).</b> Both routes now
/// require actor provenance (<see cref="ActorProvenanceGate"/>, ADR-0006 Decision 3), a live
/// tenant-scoped permission grant (<see cref="IPermissionGateway"/>, distinct read/write keys),
/// and target tenant existence (<see cref="ITenantExistenceGateway"/>) before reaching the
/// service - closing "any caller can read/write any tenant's feature posture by self-asserting
/// an actor id." Order: provenance (who is calling) -&gt; permission (may this actor act on
/// this tenant) -&gt; tenant existence (does the target even exist) - permission is checked
/// before existence so an unauthorized caller cannot use this endpoint as a tenant-id-existence
/// oracle.
/// </summary>
public static class TenantFeatureConfigurationEndpoints
{
    private const string BasePath = "/api/v1/tenants/{tenantId}/feature-configuration";

    public const string ReadPermissionKey = "tenant_feature_configuration.read";
    public const string ManagePermissionKey = "tenant_feature_configuration.manage";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(BasePath, GetAsync);
        endpoints.MapPatch(BasePath, UpdateAsync);
    }

    private static Task GetAsync(
        HttpContext context, TenantFeatureConfigurationService service, IPermissionGateway permissionGateway,
        ActorProvenanceGate provenanceGate, ITenantExistenceGateway tenantExistenceGateway, string tenantId) => Handle(context, async _ =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        provenanceGate.Require(context.Request, actor.Id);

        if (!await permissionGateway.ActorHasPermissionAsync(tenantId, actor.Id, ReadPermissionKey, context.RequestAborted))
        {
            throw TenantFeatureConfigurationError.Forbidden(ReadPermissionKey);
        }

        if (!await tenantExistenceGateway.ExistsAsync(tenantId, context.RequestAborted))
        {
            throw TenantFeatureConfigurationError.TenantNotFound(tenantId);
        }

        var config = await service.GetEffectiveAsync(tenantId, context.RequestAborted);
        return Results.Ok(TenantFeatureConfigurationResponse.From(config));
    });

    private static Task UpdateAsync(
        HttpContext context, TenantFeatureConfigurationService service, IPermissionGateway permissionGateway,
        ActorProvenanceGate provenanceGate, ITenantExistenceGateway tenantExistenceGateway, string tenantId) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        provenanceGate.Require(context.Request, actor.Id);

        if (!await permissionGateway.ActorHasPermissionAsync(tenantId, actor.Id, ManagePermissionKey, context.RequestAborted))
        {
            throw TenantFeatureConfigurationError.Forbidden(ManagePermissionKey);
        }

        if (!await tenantExistenceGateway.ExistsAsync(tenantId, context.RequestAborted))
        {
            throw TenantFeatureConfigurationError.TenantNotFound(tenantId);
        }

        var request = await ReadJsonAsync<UpdateTenantFeatureConfigurationRequest>(context);
        var config = await service.UpdateAsync(tenantId, request, actor, correlationId, context.RequestAborted);
        return Results.Ok(TenantFeatureConfigurationResponse.From(config));
    });

    private static async Task<T> ReadJsonAsync<T>(HttpContext context)
    {
        try
        {
            var body = await JsonSerializer.DeserializeAsync<T>(context.Request.Body, TenantFeatureConfigJsonOptions.Strict, context.RequestAborted);
            if (body is null)
            {
                throw TenantFeatureConfigurationError.Validation([new ValidationErrorItem("", "required", "a JSON request body is required")]);
            }

            return body;
        }
        catch (JsonException ex)
        {
            throw TenantFeatureConfigurationError.Validation([new ValidationErrorItem("", "invalid_format", $"request body is not valid JSON for this endpoint: {ex.Message}")]);
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
        catch (TenantFeatureConfigurationError error)
        {
            await ProblemResponse.WriteAsync(context, error, correlationId);
        }
    }
}
