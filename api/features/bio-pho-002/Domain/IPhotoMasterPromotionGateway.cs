using Biolog.Features.PhotoBooth.Audit;

namespace Biolog.Features.PhotoBooth.Domain;

/// <summary>
/// This feature's own narrow seam for the one thing an approval actually does: append the
/// approved capture's content as a new, immutable <see cref="Domain.CaptureStatus.Promoted"/>-
/// bound <c>PhotoMaster</c> version (BIO-PHO-001) for the same person. Deliberately NOT a
/// <c>ProjectReference</c> to <c>api/features/bio-pho-001</c> - the real implementation is an
/// adapter in <c>Biolog.Api.Modules.BioPho002Module</c> wrapping BIO-PHO-001's own
/// already-registered <c>PhotoMasterService.AppendAsync</c>, so promotion runs through the
/// exact same immutability/versioning/audit machinery BIO-PHO-001-AC01 already established -
/// this story never reimplements or bypasses it.
/// </summary>
public interface IPhotoMasterPromotionGateway
{
    /// <summary>Returns the new PhotoMaster's version number.</summary>
    Task<int> PromoteAsync(string personId, byte[] content, AuditActor actor, string correlationId, CancellationToken cancellationToken = default);
}
