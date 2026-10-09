using System.Text.Json;
using Biolog.Features.Rbac.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Biolog.Features.Rbac.Api;

/// <summary>
/// <c>/api/v1/persons/{personId}/roles</c> and <c>/api/v1/roles</c> (ADR-0004 §1.1 versioning) -
/// tenant-plane, nested under the owning person for assignment, matching BIO-IDN-002/003's
/// convention; the catalogue itself is a fixed, non-tenant-scoped listing. Every mutating route
/// requires an asserted actor (<see cref="ActorResolver"/>) and, since BIO-IDN-006-QA-08, a
/// verified provenance proof for that actor id (<see cref="ActorProvenanceGate"/>, ADR-0006
/// Decision 3).
/// </summary>
public static class RoleAssignmentEndpoints
{
    private const string AssignmentsBasePath = "/api/v1/persons/{personId}/roles";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/roles", GetCatalogueAsync);

        var assignments = endpoints.MapGroup(AssignmentsBasePath);
        assignments.MapPost("/{role}", AssignAsync);
        assignments.MapPut("/{role}/revoke", RevokeAsync);
        assignments.MapGet("", ListAsync);
    }

    private static Task GetCatalogueAsync(HttpContext context) => Handle(context, _ => Task.FromResult<IResult>(
        Results.Ok(new RoleCatalogueResponse { Data = [.. RoleTypeExtensions.All.Select(r => new RoleCatalogueEntry { Role = r.ToWireString() })] })));

    private static Task AssignAsync(HttpContext context, RoleAssignmentService service, ActorProvenanceGate provenanceGate, string personId, string role) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        provenanceGate.Require(context.Request, actor.Id);
        var parsedRole = ParseRoleOrThrow(role);
        var assignment = await service.AssignAsync(personId, parsedRole, actor, correlationId, context.RequestAborted);
        return Results.Ok(PersonRoleAssignmentResponse.From(assignment));
    });

    private static Task RevokeAsync(HttpContext context, RoleAssignmentService service, ActorProvenanceGate provenanceGate, string personId, string role) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        provenanceGate.Require(context.Request, actor.Id);
        var parsedRole = ParseRoleOrThrow(role);
        var request = await ReadJsonAsync<RevokeRoleRequest>(context);
        var assignment = await service.RevokeAsync(personId, parsedRole, request, actor, correlationId, context.RequestAborted);
        return Results.Ok(PersonRoleAssignmentResponse.From(assignment));
    });

    private static Task ListAsync(HttpContext context, RoleAssignmentService service, string personId) => Handle(context, async _ =>
    {
        var assignments = await service.ListForPersonAsync(personId, context.RequestAborted);
        return Results.Ok(new PersonRoleAssignmentListResponse { Data = [.. assignments.Select(PersonRoleAssignmentResponse.From)] });
    });

    private static RoleType ParseRoleOrThrow(string role) =>
        RoleTypeExtensions.TryParse(role, out var parsed) ? parsed : throw RbacError.Validation([new ValidationErrorItem("role", "invalid_format", "role must be one of the eleven baseline roles")]);

    internal static async Task<T> ReadJsonAsync<T>(HttpContext context)
    {
        try
        {
            var body = await JsonSerializer.DeserializeAsync<T>(context.Request.Body, RbacJsonOptions.Strict, context.RequestAborted);
            if (body is null)
            {
                throw RbacError.Validation([new ValidationErrorItem("", "required", "a JSON request body is required")]);
            }

            return body;
        }
        catch (JsonException ex)
        {
            throw RbacError.Validation([new ValidationErrorItem("", "invalid_format", $"request body is not valid JSON for this endpoint: {ex.Message}")]);
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
        catch (RbacError error)
        {
            await ProblemResponse.WriteAsync(context, error, correlationId);
        }
    }
}
