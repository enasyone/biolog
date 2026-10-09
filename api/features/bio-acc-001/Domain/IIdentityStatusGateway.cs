namespace Biolog.Features.AccessPolicy.Domain;

/// <summary>
/// AC04's own narrow seam for "what is the evaluation subject's current lifecycle status" -
/// resolved LIVE from BIO-IDN-001 (a reachable, authoritative source of record this project may
/// gateway to, the same "PersonRegistry" wiring every identity-wave story already uses), never
/// accepted as a caller-supplied value the way <see cref="PolicyEvaluationContext.PresentedCardStatus"/>
/// is - a caller-supplied identity status would be a pointless, spoofable duplicate of a fact the
/// system already owns. <c>Biolog.Api</c>'s <c>BioAcc001Module</c> implements this by wrapping
/// BIO-IDN-001's own already-registered <c>PersonService.GetAsync</c> directly. <c>null</c> means
/// "no status could be resolved" (person not found, or a wire-string this project's
/// <see cref="IdentityStatus"/> mirror does not recognise) - <see cref="PolicyEvaluation.PolicyEvaluator"/>
/// treats an unresolvable identity status as never matching any rule's
/// <see cref="IdentityStatusMatch"/>, per the fail-closed default AC03 requires.
/// </summary>
internal interface IIdentityStatusGateway
{
    Task<IdentityStatus?> GetStatusAsync(string personId, CancellationToken cancellationToken = default);
}
