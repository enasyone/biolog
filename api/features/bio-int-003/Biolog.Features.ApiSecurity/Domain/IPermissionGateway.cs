namespace Biolog.Features.ApiSecurity;

/// <summary>
/// Security-review fix (Zainab, ZB-INT003-02, Critical): this story's RBAC integration seam -
/// create/list/rotate-secret/revoke are all authorization decisions, gated by whether the
/// calling actor already holds the required permission, never an open surface reachable by
/// anyone who merely resolves to a tenant. <c>Biolog.Api</c>'s <c>BioInt003Module</c> implements
/// this by wrapping BIO-IDN-006's own already-registered
/// <c>PermissionService.ActorHasPermissionAsync</c> directly (that method's own header: never
/// throws for a malformed/nonexistent actor id, simply reports "not granted" - the correct
/// signal for an authorization gate) - the same seam BIO-ACC-001's own <c>IPermissionGateway</c>
/// already established and this project copies rather than invents independently.
/// </summary>
internal interface IPermissionGateway
{
    Task<bool> ActorHasPermissionAsync(string tenantId, string actorId, string permissionKey, CancellationToken cancellationToken = default);
}

/// <summary>Thrown by <see cref="ServiceAccountService"/> when the resolved actor does not hold
/// the permission a management/read operation requires (BIO-INT-003-ZB-02). Maps to HTTP 403 in
/// <c>BioInt003Module</c>.</summary>
public sealed class PermissionRequiredException(string permissionKey) : Exception(
    $"this action requires the calling actor to hold the '{permissionKey}' permission")
{
    public string PermissionKey { get; } = permissionKey;
}
