namespace Biolog.Features.TransportNotifications;

/// <summary>
/// This story's RBAC integration seam. Unlike bio-trn-002's own <c>TransportNotificationConfig</c>
/// (deliberately left ungated - see that type's own header: it only controls whether an internal
/// log record is written, "grants no differential access to anyone and gates no physical action"),
/// THIS story's rules control a real delivery decision (who/what gets notified, with what
/// minimized content) - a genuine interpretive authority, the same class of decision
/// <c>AttendancePolicy</c>/<c>VisitorTenantConfig</c>/BIO-INT-004's own webhook subscriptions
/// already gate. <c>Biolog.Api</c>'s <c>BioTrn003Module</c> implements this by wrapping
/// BIO-IDN-006's own already-registered <c>PermissionService.ActorHasPermissionAsync</c> directly -
/// the same seam BIO-ACC-001's/BIO-INT-003's/BIO-INT-004's own <c>IPermissionGateway</c> already
/// established and this project copies rather than invents independently.
/// </summary>
internal interface IPermissionGateway
{
    Task<bool> ActorHasPermissionAsync(string tenantId, string actorId, string permissionKey, CancellationToken cancellationToken = default);
}

/// <summary>Thrown by <see cref="TransportNotificationRuleService"/>/<see cref="TransportNotificationDispatchService"/>
/// when the resolved actor does not hold the permission a management/read operation requires.
/// Maps to HTTP 403 in <c>BioTrn003Module</c>.</summary>
public sealed class PermissionRequiredException(string permissionKey) : Exception(
    $"this action requires the calling actor to hold the '{permissionKey}' permission")
{
    public string PermissionKey { get; } = permissionKey;
}
