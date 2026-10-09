using Biolog.Features.ImportConflictReview.Domain;

namespace Biolog.Features.ImportConflictReview.Tests;

/// <summary>
/// A per-test, directly-settable stand-in for BIO-IDN-006's real RBAC engine - proves the
/// positive-grant path at the service level, the same disclosed-limitation shape BIO-INT-003/004's
/// own README already established for a story whose test project has no InternalsVisibleTo access
/// to seed a real grant.
///
/// ZB-INT005-01/ZB-INT005-02 round-1 remediation: grants PER PERMISSION KEY, not a single
/// blanket flag - scan/list now require <see cref="ImportConflictReviewService.ReadPermission"/>
/// and decide requires <see cref="ImportConflictReviewService.DecidePermission"/>, two
/// independent gates a test needs to deny separately (e.g. "can read the queue but cannot
/// decide").
/// </summary>
public sealed class FakePermissionGateway(IReadOnlySet<string>? deniedPermissionKeys = null) : IPermissionGateway
{
    public Task<bool> ActorHasPermissionAsync(string tenantId, string actorId, string permissionKey, CancellationToken cancellationToken = default) =>
        Task.FromResult(deniedPermissionKeys is null || !deniedPermissionKeys.Contains(permissionKey));
}
