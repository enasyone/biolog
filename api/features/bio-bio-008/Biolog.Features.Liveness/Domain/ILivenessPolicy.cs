namespace Biolog.Features.Liveness.Domain;

/// <summary>
/// BIO-BIO-008-AC03: "Policy can require liveness for selected workflows." Tenant-scoped and
/// keyed by a plain workflow identifier (see <see cref="LivenessWorkflows"/>) - which workflows
/// count as "high-assurance" and therefore need liveness evidence is a per-tenant configuration
/// decision, not something this feature hard-codes for every tenant identically.
///
/// This answers ONLY "is liveness required here" - it does not itself gate or block anything.
/// Whether a caller that finds liveness required, then evaluates it, and gets back a non-Live
/// <see cref="LivenessEvidence"/> actually stops the workflow is a decision for that CALLING
/// workflow (a future story), the same "purely informational, no decision concept" boundary
/// BIO-BIO-007's own search service already established for its own different question.
/// </summary>
internal interface ILivenessPolicy
{
    Task<bool> IsRequiredAsync(string tenantId, string workflow, CancellationToken cancellationToken);
}

/// <summary>Well-known workflow identifiers this feature's own callers/tests use - plain strings
/// (no identifier-registry.json prefix exists for a workflow name; out of this story's frozen-
/// contract write scope, the same explicitly documented choice every prior BIO-BIO story makes
/// for its own plain-string identifiers).</summary>
internal static class LivenessWorkflows
{
    public const string FaceEnrolment = "face_enrolment";
    public const string FaceVerification1to1 = "face_verification_1to1";
    public const string FaceIdentification1toN = "face_identification_1toN";
}
