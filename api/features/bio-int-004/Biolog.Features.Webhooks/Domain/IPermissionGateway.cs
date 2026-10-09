namespace Biolog.Features.Webhooks;

/// <summary>
/// This story's RBAC integration seam - create/list/update/delete subscription are all
/// authorization decisions, gated by whether the calling actor already holds the required
/// permission, never an open surface reachable by anyone who merely resolves to a tenant.
/// <c>Biolog.Api</c>'s <c>BioInt004Module</c> implements this by wrapping BIO-IDN-006's own
/// already-registered <c>PermissionService.ActorHasPermissionAsync</c> directly - the same
/// seam BIO-ACC-001's/BIO-INT-003's own <c>IPermissionGateway</c> already established and this
/// project copies rather than invents independently.
/// </summary>
internal interface IPermissionGateway
{
    Task<bool> ActorHasPermissionAsync(string tenantId, string actorId, string permissionKey, CancellationToken cancellationToken = default);
}

/// <summary>Thrown by <see cref="WebhookSubscriptionService"/> when the resolved actor does not
/// hold the permission a management/read operation requires. Maps to HTTP 403 in <c>BioInt004Module</c>.</summary>
public sealed class PermissionRequiredException(string permissionKey) : Exception(
    $"this action requires the calling actor to hold the '{permissionKey}' permission")
{
    public string PermissionKey { get; } = permissionKey;
}
