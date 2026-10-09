namespace Biolog.Features.FaceVerification.Domain;

/// <summary>
/// AC03: "Verification evidence is separate from authorization decision." A pure, static,
/// dependency-free function - deliberately NOT a method on <see cref="Service.FaceVerificationService"/>
/// and deliberately taking no I/O of its own - so the separation AC03 asks for is architectural,
/// not just two fields on one record: producing evidence (a biometric fact) and deciding
/// authorization (a policy judgement about that fact) are two different operations a caller can
/// invoke independently, at different times, under different policy versions, without ever
/// re-running a biometric comparison.
///
/// Today's rule is intentionally simple - <see cref="FaceComparisonOutcome.Matched"/> is the only
/// input that can produce <see cref="AccessDecision.Allow"/> - because none of this story's four
/// ACs ask for anything richer (e.g. an identity-status gate the way BIO-BIO-004-AC04 does for
/// fingerprints). A later story that wants a richer authorization policy changes only this
/// function's body; neither <see cref="FaceVerificationEvidence"/> nor
/// <see cref="Service.FaceVerificationService.CompareAsync"/> need to change at all.
/// </summary>
public static class FaceAuthorizationPolicy
{
    public static FaceAuthorizationDecision Decide(FaceVerificationEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        return evidence.ComparisonOutcome switch
        {
            FaceComparisonOutcome.Matched => new FaceAuthorizationDecision { EvidenceId = evidence.Id, Decision = AccessDecision.Allow },
            FaceComparisonOutcome.NotMatched => new FaceAuthorizationDecision
            {
                EvidenceId = evidence.Id, Decision = AccessDecision.Deny, DenyReason = "no_match",
            },
            FaceComparisonOutcome.AcquisitionFailed => new FaceAuthorizationDecision
            {
                EvidenceId = evidence.Id, Decision = AccessDecision.Deny, DenyReason = "acquisition_failed",
            },
            FaceComparisonOutcome.ReferenceStale => new FaceAuthorizationDecision
            {
                EvidenceId = evidence.Id, Decision = AccessDecision.Deny, DenyReason = "reference_stale",
            },
            _ => throw new ArgumentOutOfRangeException(nameof(evidence), evidence.ComparisonOutcome, "unreachable: exhaustive switch"),
        };
    }
}
