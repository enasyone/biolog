using Biolog.Features.Rbac.Audit;
using Biolog.Features.Rbac.Domain;
using Microsoft.AspNetCore.Http;

namespace Biolog.Features.Rbac.Api;

/// <summary>
/// No authentication story has landed yet (BIO-INT-001, W12) - every mutating endpoint here
/// requires the caller to name themselves explicitly via <c>X-Actor-Id</c>, the same disclosed
/// seam every sibling story in this epic already uses, pending BIO-INT-001. BIO-IDN-006 itself
/// is the RBAC catalogue BIO-INT-001 depends on (real authentication maps its own provider
/// claims onto the roles/permissions this story defines).
///
/// <b>BIO-IDN-006-QA-01/QA-02/QA-03/QA-04/QA-07 (Seyi, rounds 1-4).</b> This story's own
/// mutating endpoints ARE now gated behind its own permission model - role assignment,
/// permission-grant configuration, sensitive-action-rule configuration, and maker-checker
/// participation all require the asserted actor to already hold real authority (see
/// <see cref="RoleAssignmentService"/>'s header for the full history). That authorization is
/// still only as trustworthy as the identity this method resolves.
///
/// <b>BIO-IDN-006-QA-08 (Zainab's security review, Critical; ADR-0006 Decision 3).</b>
/// <c>X-Actor-Id</c> remains a caller-supplied ASSERTION with no authenticated principal
/// binding - a caller who asserts a KNOWN or GUESSED privileged person id is authorized as that
/// person, since authorization can only ever be as strong as the identity it trusts.
/// <see cref="ActorProvenanceGate"/> is the resulting interim mitigation (checked immediately
/// AFTER this method resolves the value, before any authorization decision uses it, on every
/// mutation the paragraph above lists) - see its own header for the full ADR-0006 rationale and
/// disclosed residual scope. The real fix - binding this resolved identity to something actually
/// authenticated - remains BIO-INT-001's job; see docs/adr/ADR-0006-actor-identity-and-authorization-boundary.md.
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
            throw RbacError.ActorRequired();
        }

        var actorTypeRaw = request.Headers[ActorTypeHeader].ToString();
        var actorType = ActorType.User;
        if (!string.IsNullOrEmpty(actorTypeRaw) && !ActorTypeExtensions.TryParse(actorTypeRaw, out actorType))
        {
            throw RbacError.InvalidActorType(actorTypeRaw);
        }

        return new AuditActor(actorType, actorId);
    }
}
