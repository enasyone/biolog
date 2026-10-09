namespace Biolog.Features.BiometricRetention.Domain;

/// <summary>
/// Thrown only for a genuinely un-evaluable request - never for a routine, meaningful outcome.
/// A deletion that finds nothing to delete, or one the underlying store cannot yet perform, is a
/// typed <see cref="DeletionOutcome"/>, not an exception (the same typed-outcome-vs-exception
/// discipline this author's own BIO-BIO-004/BIO-BIO-008 stories already established).
/// </summary>
internal sealed class BiometricRetentionException : InvalidOperationException
{
    public string Code { get; }

    private BiometricRetentionException(string code, string message) : base(message)
    {
        Code = code;
    }

    /// <summary>AC01: a retention policy with a non-positive day count is not a configuration
    /// choice anyone can mean - "retain for -3 days" or "retain for 0 days" has no sensible
    /// reading distinct from "delete immediately", which this story expresses through deletion,
    /// not a retention day count.</summary>
    public static BiometricRetentionException InvalidRetentionDays(int retentionDays) => new(
        "invalid_retention_days", $"retention days must be positive; received {retentionDays}.");

    /// <summary>The caller did not assert the specific permission this mutation requires via
    /// <c>X-Actor-Permission</c> (ADR-0006's asserted-not-verified accommodation, same shape
    /// BIO-IDN-004-AC03 already established).</summary>
    public static BiometricRetentionException PermissionRequired(string requiredPermission) => new(
        "permission_required",
        $"this action requires the caller to assert the '{requiredPermission}' permission via X-Actor-Permission.");

    /// <summary>ADR-0003 Decision 7, element 4: the audit sink's write threw and verification
    /// could not confirm the event landed anyway.</summary>
    public static BiometricRetentionException AuditUnavailable(string detail) => new(
        "audit_unavailable", $"audit sink write failed: {detail}");

    /// <summary>No authentication story has landed yet (BIO-INT-001) - every mutating endpoint
    /// requires the caller to name themselves explicitly via <c>X-Actor-Id</c>, the same
    /// disclosed seam BIO-IDN-004's own <c>ActorResolver</c> already uses.</summary>
    public static BiometricRetentionException ActorRequired() => new(
        "actor_required", "this action requires the caller to assert their identity via X-Actor-Id.");

    /// <summary>ADR-0006 Decision 3: the request did not present a valid <c>X-Actor-Provenance</c>
    /// proof binding it to the asserted actor id - required before any permission lookup is
    /// trusted for an authorization-decision use of the asserted-actor accommodation.</summary>
    public static BiometricRetentionException ActorProvenanceRequired() => new(
        "actor_provenance_required", "this action requires a valid X-Actor-Provenance proof for the asserted actor.");
}
