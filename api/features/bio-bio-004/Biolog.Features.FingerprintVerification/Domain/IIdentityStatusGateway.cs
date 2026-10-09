namespace Biolog.Features.FingerprintVerification.Domain;

/// <summary>
/// AC04's own narrow seam for "what is the claimed identity's current lifecycle status" -
/// deliberately NOT a <c>ProjectReference</c> to <c>api/features/bio-idn-001</c> (this project's
/// csproj remarks explain why). The real implementation is an adapter in
/// <c>Biolog.Api.Modules.BioBio004Module</c> wrapping BIO-IDN-001's own already-registered
/// <c>PersonService.GetAsync</c> - the same shape BIO-BIO-003/BIO-PHO-001/BIO-PHO-002/
/// BIO-IDN-002 through 006 already use for their own read-only gateways.
///
/// A <c>null</c> result means "no status could be resolved for this identity" (not found, or a
/// wire-string this project's <see cref="IdentityStatus"/> mirror does not recognise) -
/// <see cref="Service.FingerprintVerificationService"/> treats that the same as any status other
/// than <see cref="IdentityStatus.Suspended"/>/<see cref="IdentityStatus.Revoked"/>: AC04 asks
/// only that suspended/revoked identities cannot reach an access allow by match alone, not that
/// this story invent a broader identity-existence policy no acceptance criterion asks for (see
/// docs/features/bio-bio-004/README.md).
/// </summary>
public interface IIdentityStatusGateway
{
    Task<IdentityStatus?> GetStatusAsync(string personId, CancellationToken cancellationToken = default);
}
