using Biolog.Features.Rbac.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Biolog.Features.Rbac.Api;

/// <summary>
/// <c>/api/v1/roles/{role}/permissions/{permissionKey}</c> (tenant-scoped grant configuration,
/// AC02) and <c>/api/v1/persons/{personId}/permissions/{permissionKey}</c> (the real
/// verification surface, <see cref="PermissionService.HasPermissionAsync"/>). The grant-write
/// route additionally requires a verified provenance proof since BIO-IDN-006-QA-08 (<see cref="ActorProvenanceGate"/>, ADR-0006 Decision 3).
/// </summary>
public static class PermissionEndpoints
{
    private const string GrantsBasePath = "/api/v1/roles/{role}/permissions/{permissionKey}";
    private const string CheckBasePath = "/api/v1/persons/{personId}/permissions/{permissionKey}";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPut(GrantsBasePath, SetGrantAsync);
        endpoints.MapGet(GrantsBasePath, GetGrantAsync);
        endpoints.MapGet(CheckBasePath, CheckAsync);
    }

    private static Task SetGrantAsync(HttpContext context, PermissionService service, ActorProvenanceGate provenanceGate, string role, string permissionKey) =>
        RoleAssignmentEndpoints.Handle(context, async correlationId =>
        {
            var actor = ActorResolver.Resolve(context.Request);
            provenanceGate.Require(context.Request, actor.Id);
            var parsedRole = ParseRoleOrThrow(role);
            var request = await RoleAssignmentEndpoints.ReadJsonAsync<SetPermissionGrantRequest>(context);
            var grant = await service.SetGrantAsync(parsedRole, permissionKey, request, actor, correlationId, context.RequestAborted);
            return Results.Ok(RolePermissionGrantResponse.From(parsedRole, permissionKey, grant));
        });

    private static Task GetGrantAsync(HttpContext context, PermissionService service, string role, string permissionKey) =>
        RoleAssignmentEndpoints.Handle(context, async _ =>
        {
            var parsedRole = ParseRoleOrThrow(role);
            var grant = await service.GetGrantAsync(parsedRole, permissionKey, context.RequestAborted);
            return Results.Ok(RolePermissionGrantResponse.From(parsedRole, permissionKey, grant));
        });

    private static Task CheckAsync(HttpContext context, PermissionService service, string personId, string permissionKey) =>
        RoleAssignmentEndpoints.Handle(context, async _ =>
        {
            var (granted, viaRoles) = await service.HasPermissionAsync(personId, permissionKey, context.RequestAborted);
            return Results.Ok(new PermissionCheckResponse
            {
                PersonId = personId, PermissionKey = permissionKey, Granted = granted, ViaRoles = [.. viaRoles.Select(r => r.ToWireString())],
            });
        });

    private static RoleType ParseRoleOrThrow(string role) =>
        RoleTypeExtensions.TryParse(role, out var parsed) ? parsed : throw RbacError.Validation([new ValidationErrorItem("role", "invalid_format", "role must be one of the eleven baseline roles")]);
}
