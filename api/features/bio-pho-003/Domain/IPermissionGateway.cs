namespace Biolog.Features.PhotoQuality.Domain;

/// <summary>
/// This story's own RBAC integration seam, wrapping BIO-IDN-006's own already-registered,
/// internal <c>PermissionService.ActorHasPermissionAsync</c> (the same wrapping shape
/// BIO-BIO-009's/BIO-ATT-003's own identically-named interface already established) - a real,
/// tenant-scoped grant lookup, never a bare caller-asserted permission claim. ZB-PHO003-01
/// (Zainab, security round 1): every mutating AND every reading route in this feature is an
/// authorization decision (unlike BIO-ATT-003's own capture/read split) - biometric-suitability
/// verdicts, operator decision reasons and photo metadata are sensitive enough that a read
/// requires the same real grant check as a write, just a lesser-privileged permission key
/// (<see cref="PhotoQualityPermissions.View"/>).
/// </summary>
public interface IPermissionGateway
{
    Task<bool> ActorHasPermissionAsync(string tenantId, string actorId, string permissionKey, CancellationToken cancellationToken = default);
}

/// <summary>The three least-privilege permission keys this story's five routes require - submitting a candidate photo, deciding (approve/reject) a pending one, and reading capture data are three distinct authorization decisions, not one bundled "can touch photo quality" grant.</summary>
public static class PhotoQualityPermissions
{
    public const string Submit = "photo_quality.capture.submit";
    public const string Decide = "photo_quality.capture.decide";
    public const string View = "photo_quality.capture.view";
}
