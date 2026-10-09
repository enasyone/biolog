using System.Text.Json;
using Biolog.Features.TenantRegistry.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Biolog.Features.TenantRegistry.Api;

/// <summary>
/// BIO-TEN-001-AC01: Tenant CRUD, control-plane scoped. Every route here is versioned
/// under <c>/api/v1</c> (ADR-0004 Decision 1.1) and lives under <c>/control-plane</c>,
/// mirroring ADR-0002 §1.4's distinct control-plane context - nothing here resolves,
/// accepts or forwards a tenant context. None of these routes require
/// <c>Idempotency-Key</c>: ADR-0004 §4.3 names card activation, production completion,
/// offline sync intake and webhook intake as the required set, and admin tenant CRUD is
/// none of those - a later story may opt this surface in the same way ADR-0004 allows
/// any write endpoint to.
/// </summary>
public static class TenantEndpoints
{
    private const string BasePath = "/api/v1/control-plane/tenants";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(BasePath);

        group.MapPost("", CreateAsync);
        group.MapGet("", ListAsync);
        group.MapGet("/{id}", GetAsync);
        group.MapPatch("/{id}", UpdateAsync);
        group.MapPut("/{id}/status", UpdateStatusAsync);
        group.MapDelete("/{id}", DeleteAsync);
    }

    private static Task CreateAsync(HttpContext context, TenantService service) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        var request = await ReadJsonAsync<CreateTenantRequest>(context);
        var tenant = await service.CreateAsync(request, actor, correlationId, context.RequestAborted);
        return Results.Created($"{BasePath}/{tenant.Id}", TenantResponse.From(tenant));
    });

    private static Task GetAsync(HttpContext context, TenantService service, string id) => Handle(context, async _ =>
    {
        var tenant = await service.GetAsync(id, context.RequestAborted) ?? throw TenantRegistryError.NotFound(id);
        return Results.Ok(TenantResponse.From(tenant));
    });

    private static Task ListAsync(HttpContext context, TenantService service) => Handle(context, async _ =>
    {
        var query = context.Request.Query;
        int? limit = int.TryParse(query["limit"], out var parsedLimit) ? parsedLimit : null;
        var cursor = query["cursor"].ToString();
        var status = query["status"].ToString();

        var (items, nextCursor) = await service.ListAsync(
            limit, string.IsNullOrEmpty(cursor) ? null : cursor, string.IsNullOrEmpty(status) ? null : status, context.RequestAborted);

        return Results.Ok(new TenantListResponse
        {
            Data = [.. items.Select(TenantResponse.From)],
            Page = new PageInfo { NextCursor = nextCursor, Limit = limit ?? TenantService.DefaultLimit },
        });
    });

    private static Task UpdateAsync(HttpContext context, TenantService service, string id) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        var request = await ReadJsonAsync<UpdateTenantRequest>(context);
        var tenant = await service.UpdateAsync(id, request, actor, correlationId, context.RequestAborted);
        return Results.Ok(TenantResponse.From(tenant));
    });

    private static Task UpdateStatusAsync(HttpContext context, TenantService service, string id) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        var request = await ReadJsonAsync<UpdateTenantStatusRequest>(context);
        var tenant = await service.UpdateStatusAsync(id, request, actor, correlationId, context.RequestAborted);
        return Results.Ok(TenantResponse.From(tenant));
    });

    private static Task DeleteAsync(HttpContext context, TenantService service, string id) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        if (!int.TryParse(context.Request.Query["version"], out var version))
        {
            throw TenantRegistryError.Validation([new ValidationErrorItem("version", "required", "the ?version= query parameter is required so a stale delete can be detected")]);
        }

        var tenant = await service.SoftDeleteAsync(id, version, actor, correlationId, context.RequestAborted);
        return Results.Ok(TenantResponse.From(tenant));
    });

    private static async Task<T> ReadJsonAsync<T>(HttpContext context)
    {
        try
        {
            var body = await JsonSerializer.DeserializeAsync<T>(context.Request.Body, TenantJsonOptions.Strict, context.RequestAborted);
            if (body is null)
            {
                throw TenantRegistryError.Validation([new ValidationErrorItem("", "required", "a JSON request body is required")]);
            }

            return body;
        }
        catch (JsonException ex)
        {
            throw TenantRegistryError.Validation([new ValidationErrorItem("", "invalid_format", $"request body is not valid JSON for this endpoint: {ex.Message}")]);
        }
    }

    /// <summary>
    /// Resolves the correlation id (ADR-0004 §3.2), runs <paramref name="action"/>, always
    /// echoes the resolved id back in <c>X-Correlation-Id</c> (success or failure), and maps
    /// a <see cref="TenantRegistryError"/> to a problem+json response instead of letting it
    /// become an unhandled 500.
    /// </summary>
    private static async Task Handle(HttpContext context, Func<string, Task<IResult>> action)
    {
        var correlationId = CorrelationIdHeader.Resolve(context.Request);
        context.Response.Headers[CorrelationIdHeader.HeaderName] = correlationId;

        try
        {
            var result = await action(correlationId);
            await result.ExecuteAsync(context);
        }
        catch (TenantRegistryError error)
        {
            await ProblemResponse.WriteAsync(context, error, correlationId);
        }
    }
}
