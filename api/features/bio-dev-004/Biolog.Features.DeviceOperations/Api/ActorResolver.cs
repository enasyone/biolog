using Microsoft.AspNetCore.Http;

namespace Biolog.Features.DeviceOperations.Api;

using Biolog.Features.DeviceOperations;

/// <summary>Thrown when <c>X-Actor-Id</c> is missing on any route. Maps to HTTP 400 in <c>BioDev004Module</c>.</summary>
public sealed class ActorRequiredException() : Exception("X-Actor-Id is required to perform this action");

/// <summary>Thrown when <c>X-Actor-Type</c> is present but not one of the known wire values. Maps to HTTP 400 in <c>BioDev004Module</c>.</summary>
public sealed class InvalidActorTypeException(string actorType) : Exception(
    $"X-Actor-Type must be one of user, service_account, device, system, anonymous, unknown (got '{actorType}')");

/// <summary>
/// The same disclosed ADR-0006 Decision 3 seam every sibling story in this codebase uses
/// (BIO-TRN-003's own <c>ActorResolver</c>, copied here rather than shared - this project
/// cannot reference that sibling feature project either): the caller names themselves
/// explicitly via <c>X-Actor-Id</c>, checked immediately afterward by
/// <see cref="ActorProvenanceGate"/> before any authorization decision trusts the value.
///
/// SY-DEV004-01 (Seyi, round 1): before this fix, <c>DeviceHeartbeatRequest</c>/
/// <c>RemoteJobRequest</c> accepted <c>ActorType</c>/<c>ActorId</c> directly from the request
/// JSON body, with no provenance check at all - a caller could attribute a real remote reboot/
/// configuration/firmware job to any actor it chose. This resolver is now the ONLY source of
/// actor identity for every BIO-DEV-004 route; the request body no longer carries actor fields.
/// </summary>
public static class ActorResolver
{
    public const string ActorIdHeader = "X-Actor-Id";
    public const string ActorTypeHeader = "X-Actor-Type";

    private static readonly HashSet<string> KnownActorTypes =
        ["user", "service_account", "device", "system", "anonymous", "unknown"];

    public static AuditActor Resolve(HttpRequest request)
    {
        var actorId = request.Headers[ActorIdHeader].ToString();
        if (string.IsNullOrWhiteSpace(actorId))
        {
            throw new ActorRequiredException();
        }

        var actorTypeRaw = request.Headers[ActorTypeHeader].ToString();
        var actorType = string.IsNullOrEmpty(actorTypeRaw) ? "user" : actorTypeRaw;
        if (!KnownActorTypes.Contains(actorType))
        {
            throw new InvalidActorTypeException(actorType);
        }

        return new AuditActor(actorType, actorId);
    }
}
