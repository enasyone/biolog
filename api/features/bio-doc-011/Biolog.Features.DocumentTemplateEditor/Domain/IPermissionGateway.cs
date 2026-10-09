namespace Biolog.Features.DocumentTemplateEditor.Domain;

/// <summary>This feature's own narrow seam onto BIO-IDN-006's RBAC - implemented in
/// <c>Biolog.Api.Modules.BioDoc011Module</c> by wrapping that story's own already-registered
/// <c>PermissionService</c> directly, the same cross-story gateway pattern every sibling story
/// (e.g. BIO-INT-002) uses. Added in round 2 (ZB-DOC011-01, Critical) - round 1 shipped no live
/// permission check at all.</summary>
public interface IPermissionGateway
{
    Task<bool> ActorHasPermissionAsync(string tenantId, string actorId, string permissionKey, CancellationToken cancellationToken = default);
}
