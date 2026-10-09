namespace Biolog.Features.AuthenticityAssessment.Model;

/// <summary>
/// One human override of an assessment's workflow disposition (BIO-DOC-009-AC05). A layer ON TOP
/// of <see cref="AuthenticityAssessmentResult"/>, never a mutation of it - the same two-tier shape
/// BIO-DOC-001's own <c>DocumentConflict</c>/<c>ManualReviewState</c> already established for this
/// epic ("<c>Resolve</c> stores the resolution as new fields on the mutable wrapper; it never
/// touches the underlying immutable evidence record"). Locked down the same way as every prior
/// BIO-DOC-00x result type: a public <c>init</c> would let a caller fabricate an override with no
/// real actor/reason attribution.
/// </summary>
public sealed record AuthenticityManualOverride
{
    public AuthenticityDisposition Disposition { get; internal init; }
    public string Reason { get; internal init; } = null!;
    public string OverriddenBy { get; internal init; } = null!;
    public string OverriddenAt { get; internal init; } = null!;

    internal AuthenticityManualOverride(AuthenticityDisposition disposition, string reason, string overriddenBy, string overriddenAt)
    {
        Disposition = disposition;
        Reason = reason;
        OverriddenBy = overriddenBy;
        OverriddenAt = overriddenAt;
    }
}
