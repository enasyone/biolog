namespace Biolog.Features.VisitorCredentials.Domain;

/// <summary>This feature's own narrow seam onto BIO-IDN-006's RBAC - implemented in <c>Biolog.Api.Modules.BioVis002Module</c> by wrapping that story's own already-registered <c>PermissionService</c> directly, the same cross-story gateway pattern every sibling story uses.</summary>
public interface IPermissionGateway
{
    Task<bool> ActorHasPermissionAsync(string tenantId, string actorId, string permissionKey, CancellationToken cancellationToken = default);
}
