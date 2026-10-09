using Microsoft.AspNetCore.Http;

namespace Biolog.Features.ApiSecurity.Api;

/// <summary>Thrown when <c>X-Actor-Id</c> is missing on a management/read endpoint
/// (BIO-INT-003-ZB-02). Maps to HTTP 400 in <c>BioInt003Module</c>.</summary>
public sealed class ActorRequiredException() : Exception("X-Actor-Id is required to perform this action");

/// <summary>Thrown when <c>X-Actor-Type</c> is present but not one of the known wire values.
/// Maps to HTTP 400 in <c>BioInt003Module</c>.</summary>
public sealed class InvalidActorTypeException(string actorType) : Exception(
    $"X-Actor-Type must be one of user, service_account, device, system, anonymous, unknown (got '{actorType}')");

/// <summary>
/// Security-review fix (Zainab, ZB-INT003-02, Critical): service-account create/list/rotate/
/// revoke previously required only ambient tenant context - any caller that reached a tenant
/// plane could create a SuperAdmin-scoped account, read any account's metadata, rotate another
/// integrator's secret, or revoke it. This resolver is the same disclosed ADR-0006 Decision 3
/// seam every sibling story in this codebase already uses (BIO-ACC-001's own
/// <c>ActorResolver</c>, copied here rather than shared - this project cannot reference that
/// sibling feature project either): the caller names themselves explicitly via
/// <c>X-Actor-Id</c>, checked immediately afterward by <see cref="ActorProvenanceGate"/> before
/// any authorization decision trusts the value, pending BIO-INT-001/a real trusted edge.
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
            throw new ActorRequiredException();
        }

        var actorTypeRaw = request.Headers[ActorTypeHeader].ToString();
        var actorType = ActorType.User;
        if (!string.IsNullOrEmpty(actorTypeRaw) && !ActorTypeExtensions.TryParse(actorTypeRaw, out actorType))
        {
            throw new InvalidActorTypeException(actorTypeRaw);
        }

        return new AuditActor(actorType, actorId);
    }
}
