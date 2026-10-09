using Biolog.Features.PhotoBooth.Audit;
using Biolog.Features.PhotoBooth.Domain;
using Microsoft.AspNetCore.Http;

namespace Biolog.Features.PhotoBooth.Api;

/// <summary>
/// No authentication story has landed yet (BIO-INT-001, W12) - the approve/reject endpoints
/// here require the caller to name themselves explicitly via <c>X-Actor-Id</c>, the same
/// disclosed seam every sibling story in the identity/card wave already uses, pending
/// BIO-INT-001.
///
/// <b>ADR-0006 Decision 1/2/4 applies here as the "audit-attribution" shape, not the
/// "authorization-decision" shape</b> - the same disclosed-residual category BIO-PHO-001 already
/// used for the identical reason: this feature defines no <c>RoleType</c>, permission check, or
/// any other gate that makes who may approve or reject a capture depend on the asserted actor
/// identity. A forged <c>X-Actor-Id</c> can misattribute who claims responsibility, but crosses
/// no privilege-escalation boundary, since there is no differential authority here for a forged
/// identity to escalate into. It does not trigger a security review under the same convention
/// that correctly scoped one to BIO-IDN-006 alone.
///
/// This is distinct from <see cref="DeviceAuthenticator"/> (BIO-PHO-002-AC02), which DOES verify
/// a real, provisioned credential before a device may submit a capture at all - a capture
/// SUBMISSION is gated on proof of device identity; a capture DECISION (approve/reject) is only
/// ever audit-attributed, matching BIO-PHO-001's own precedent for its own mutations.
/// </summary>
public static class ActorResolver
{
    public const string ActorIdHeader = "X-Actor-Id";
    public const string ActorTypeHeader = "X-Actor-Type";

    public static AuditActor Resolve(HttpRequest request)
    {
        var actorId = request.Headers[ActorIdHeader].ToString();
        if (string.IsNullOrWhiteSpace(actorId))
        {
            throw PhotoBoothError.ActorRequired();
        }

        var actorTypeRaw = request.Headers[ActorTypeHeader].ToString();
        var actorType = ActorType.User;
        if (!string.IsNullOrEmpty(actorTypeRaw) && !ActorTypeExtensions.TryParse(actorTypeRaw, out actorType))
        {
            throw PhotoBoothError.InvalidActorType(actorTypeRaw);
        }

        return new AuditActor(actorType, actorId);
    }
}
