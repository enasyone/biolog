using System.Text.Json;
using Biolog.Features.TenantDomainRegistry.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Biolog.Features.TenantDomainRegistry.Api;

/// <summary>
/// Two distinct route groups, mirroring the ADR-0002 §1.4 distinction this whole feature is
/// built around:
///
/// <list type="bullet">
/// <item><c>/api/v1/control-plane/tenant-domains/**</c> - administrative CRUD (register,
/// list, get, status transitions, set-primary). Requires an asserted actor
/// (<see cref="ActorResolver"/>) and produces an audit trail (BIO-TEN-002's implicit
/// continuation of BIO-TEN-001-AC04's control-plane audit posture, now formalised platform-
/// wide by ADR-0003 Decision 7).</item>
/// <item><c>/api/v1/tenant-domains/resolve</c> - NOT control-plane work, and NOT itself the
/// ADR-0002 <c>host</c> channel (<b>BIO-TEN-002-QA-01, Seyi round 1, High</b> - see below for
/// what that means and what changed). No actor, no audit - see
/// <see cref="TenantDomainService.ResolveAsync"/>'s own header for why a failed resolution is
/// not itself an audited event in this story.</item>
/// </list>
///
/// <b>BIO-TEN-002-QA-01 (Seyi round 1, High):</b> the original implementation of
/// <c>/resolve</c> read <c>Request.Query["host"]</c> - a caller-controlled query parameter,
/// which ADR-0002 §1.1 names explicitly as something that is NOT a tenant channel ("a query
/// parameter ... is not a channel"). Worse, the docs on this class described the endpoint as
/// "the actual host channel", which was false on two counts: it did not read the request's
/// real authority, and this module is not wired into <c>Biolog.Api/Program.cs</c> or
/// <c>TenantContextMiddleware</c> at all, so nothing here is reachable by a real client of the
/// packaged API today.
///
/// <b>What changed:</b> <see cref="ResolveAsync"/> now reads <c>context.Request.Host.Host</c> -
/// the request's real HTTP <c>Host</c> header, the one legitimate source ADR-0002 §1.1
/// describes for the <c>host</c> channel - never a query parameter. <b>What did not change,
/// and is not claimed to have:</b> this endpoint is still not consumed by
/// <c>TenantContextMiddleware</c>; it is an admin/ops diagnostic surface that proves
/// <see cref="TenantDomainService.ResolveAsync"/>'s resolution LOGIC is correct against a
/// real Host header, not evidence that the packaged API resolves tenants from a live custom
/// hostname yet. Registering <see cref="BioTen002Module"/> into <c>Program.cs</c> and
/// connecting this method to <c>TenantContextMiddleware</c> remains the disclosed,
/// out-of-owned-paths follow-up integration step this story has documented from the start
/// (see <see cref="BioTen002Module"/>'s header and docs/features/bio-ten-002/README.md
/// "Integration seam") - the same boundary BIO-TEN-001 established and god confirmed as
/// correctly handled, not attempted unilaterally here either, pending a scope decision if
/// re-review specifically requires otherwise.
///
/// None of these routes require <c>Idempotency-Key</c>: ADR-0004 §4.3 names card activation,
/// production completion, offline sync intake and webhook intake as the required set, and
/// none of this feature's endpoints are those.
/// </summary>
public static class TenantDomainEndpoints
{
    private const string ControlPlaneBasePath = "/api/v1/control-plane/tenant-domains";
    private const string ResolveBasePath = "/api/v1/tenant-domains";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var controlPlane = endpoints.MapGroup(ControlPlaneBasePath);
        controlPlane.MapPost("", RegisterAsync);
        controlPlane.MapGet("", ListAsync);
        controlPlane.MapGet("/{hostname}", GetAsync);
        controlPlane.MapPut("/{hostname}/status", UpdateStatusAsync);
        controlPlane.MapPut("/{hostname}/primary", SetPrimaryAsync);

        endpoints.MapGroup(ResolveBasePath).MapGet("/resolve", ResolveAsync);
    }

    private static Task RegisterAsync(HttpContext context, TenantDomainService service) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        var request = await ReadJsonAsync<RegisterDomainRequest>(context);
        var domain = await service.RegisterAsync(request, actor, correlationId, context.RequestAborted);
        return Results.Created($"{ControlPlaneBasePath}/{domain.Hostname}", TenantDomainResponse.From(domain));
    });

    private static Task GetAsync(HttpContext context, TenantDomainService service, string hostname) => Handle(context, async _ =>
    {
        var domain = await service.GetAsync(hostname, context.RequestAborted) ?? throw TenantDomainError.NotFound(hostname);
        return Results.Ok(TenantDomainResponse.From(domain));
    });

    private static Task ListAsync(HttpContext context, TenantDomainService service) => Handle(context, async _ =>
    {
        var tenantId = context.Request.Query["tenant_id"].ToString();
        if (string.IsNullOrEmpty(tenantId))
        {
            throw TenantDomainError.Validation([new ValidationErrorItem("tenant_id", "required", "the ?tenant_id= query parameter is required")]);
        }

        var domains = await service.ListByTenantAsync(tenantId, context.RequestAborted);
        return Results.Ok(new TenantDomainListResponse { Data = [.. domains.Select(TenantDomainResponse.From)] });
    });

    private static Task UpdateStatusAsync(HttpContext context, TenantDomainService service, string hostname) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        var request = await ReadJsonAsync<UpdateDomainStatusRequest>(context);
        var domain = await service.UpdateStatusAsync(hostname, request, actor, correlationId, context.RequestAborted);
        return Results.Ok(TenantDomainResponse.From(domain));
    });

    private static Task SetPrimaryAsync(HttpContext context, TenantDomainService service, string hostname) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        var request = await ReadJsonAsync<SetPrimaryDomainRequest>(context);
        var domain = await service.SetPrimaryAsync(hostname, request, actor, correlationId, context.RequestAborted);
        return Results.Ok(TenantDomainResponse.From(domain));
    });

    /// <summary>
    /// BIO-TEN-002-QA-01 fix: resolves against <c>context.Request.Host.Host</c> - the real
    /// HTTP <c>Host</c> header ASP.NET Core has already parsed (port, if any, already split
    /// off by the framework's own authority parser) - never a caller-supplied query
    /// parameter. See this class's header for what this endpoint is and is not evidence of.
    /// </summary>
    private static Task ResolveAsync(HttpContext context, TenantDomainService service) => Handle(context, async _ =>
    {
        var domain = await service.ResolveAsync(context.Request.Host.Host, context.RequestAborted);
        return Results.Ok(TenantDomainResponse.From(domain));
    });

    private static async Task<T> ReadJsonAsync<T>(HttpContext context)
    {
        try
        {
            var body = await JsonSerializer.DeserializeAsync<T>(context.Request.Body, TenantDomainJsonOptions.Strict, context.RequestAborted);
            if (body is null)
            {
                throw TenantDomainError.Validation([new ValidationErrorItem("", "required", "a JSON request body is required")]);
            }

            return body;
        }
        catch (JsonException ex)
        {
            throw TenantDomainError.Validation([new ValidationErrorItem("", "invalid_format", $"request body is not valid JSON for this endpoint: {ex.Message}")]);
        }
    }

    /// <summary>
    /// Resolves the correlation id (ADR-0004 §3.2), runs <paramref name="action"/>, always
    /// echoes the resolved id back in <c>X-Correlation-Id</c> (success or failure), and maps
    /// a <see cref="TenantDomainError"/> to a problem+json response instead of letting it
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
        catch (TenantDomainError error)
        {
            await ProblemResponse.WriteAsync(context, error, correlationId);
        }
    }
}
