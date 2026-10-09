namespace Biolog.Features.PersonLifecycle.Domain;

/// <summary>
/// BIO-IDN-004-AC02: "Emergency suspension emits credential/access revocation events."
///
/// <b>Scope, disclosed honestly.</b> No credential or device-access story has a real,
/// registered <c>crd_</c> aggregate or a live revocation consumer yet - <c>BIO-DEV-001</c>'s
/// <c>Device</c>/<c>CredentialReferenceValidator</c> only validates a credential REFERENCE
/// STRING's format on device registration; it has no "revoke by person" operation, and
/// <c>BIO-CRD-001</c> is tenant brand assets, unrelated to person credentials. This story's
/// only declared dependency is BIO-IDN-001. AC02 is therefore satisfied as a genuine, distinct,
/// purpose-built EVENT EMISSION - not a call into a live downstream system that does not exist
/// yet. A future credential/device-access story can subscribe to
/// <see cref="ICredentialAccessRevocationEventSink"/> the same way it would any other durable
/// event stream; nothing here presumes what that subscriber eventually looks like.
///
/// <b>Why a SEPARATE stream from the ordinary audit trail.</b> <see cref="IPersonLifecycleAuditSink"/>
/// records EVERY lifecycle transition as one entry among many. A future credential/access
/// system does not want to scan the whole audit log filtering for suspensions - it wants a
/// purpose-built stream of exactly "revoke this person's credentials/access now" facts. This
/// event is written IN ADDITION TO, never instead of, the ordinary audit event for the same
/// suspension.
///
/// <b>BIO-IDN-004-QA-01 (Seyi round 2, High).</b> <see cref="SuspendedAtVersion"/> is the
/// person's <c>Version</c> immediately AFTER the suspending transition committed - it uniquely
/// identifies THIS specific suspension occurrence (a person's version strictly increases and is
/// CAS-checked, so no two distinct suspensions can ever share one). It is what makes
/// "does person P's most recent suspension have its revocation event recorded yet" an
/// idempotent, re-askable question - see <see cref="PersonLifecycleService"/>'s header
/// "Suspension/revocation atomicity" for the full two-round history of why this exists.
/// </summary>
public sealed record CredentialAccessRevocationEvent
{
    public required string EventId { get; init; }
    public required string TenantId { get; init; }
    public required string PersonId { get; init; }
    public required int SuspendedAtVersion { get; init; }
    public required string ReasonCode { get; init; }
    public string? Reason { get; init; }
    public required string RequestedByActorId { get; init; }
    public required string CorrelationId { get; init; }
    public required string OccurredAt { get; init; }
}
