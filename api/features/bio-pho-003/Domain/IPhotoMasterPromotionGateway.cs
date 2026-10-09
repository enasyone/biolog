using Biolog.Features.PhotoQuality.Audit;

namespace Biolog.Features.PhotoQuality.Domain;

/// <summary>
/// This feature's own narrow seam for the one thing an approval actually does: append the
/// approved capture's content as a new, immutable <c>PhotoMaster</c> version (BIO-PHO-001) for
/// the same person. Deliberately NOT a <c>ProjectReference</c> to
/// <c>api/features/bio-pho-001</c> - the real implementation is an adapter in
/// <c>Biolog.Api.Modules.BioPho003Module</c> wrapping BIO-PHO-001's own already-registered
/// <c>PhotoMasterService.AppendAsync</c>, so promotion runs through the exact same
/// immutability/versioning/audit machinery BIO-PHO-001-AC01 already established - this story
/// never reimplements or bypasses it. The identical seam shape BIO-PHO-002's own
/// <c>IPhotoMasterPromotionGateway</c> already established for its own approval action.
///
/// <b>ZB-PHO003-04 (Kemi's ruling, 2026-10-07): deliberately a strictly SMALLER interface than
/// rounds 3/4 built here - just this one method, nothing added.</b> Rounds 3 and 4 tried to
/// RECONSTRUCT "did this exact approval attempt already promote" from BIO-PHO-001's own post-hoc
/// state (an exact attempt-id audit lookup, then a repository-existence confirmation) - each fix
/// closed one failure shape and immediately exposed an adjacent one (round 5's ZB-PHO003-04:
/// confirming a version EXISTS is not confirming THIS attempt produced it), because
/// <c>PhotoMaster</c> genuinely has no caller-supplied identity field and inferring ownership
/// from an external system's own observable state can never be fully disambiguated from outside
/// it. Kemi's ruling: stop reconstructing, start REMEMBERING - give this feature its own local
/// <c>IIdempotencyStore</c> (see <c>PhotoQualityService.ApproveAsync</c>), the same shape
/// BIO-PHO-002's own TD-PHO-002-IDEMPOTENCY-RACE remediation already uses for an unrelated
/// operation. Zero change to <c>PhotoMaster</c>, its repository, or any shared contract needed.
/// </summary>
public interface IPhotoMasterPromotionGateway
{
    /// <summary>Returns the new PhotoMaster's version number.</summary>
    Task<int> PromoteAsync(string personId, byte[] content, AuditActor actor, string correlationId, CancellationToken cancellationToken = default);
}
