namespace Biolog.Features.FaceVerification.Domain;

/// <summary>
/// AC01: "Similarity, threshold policy, quality and model version are recorded." One immutable,
/// append-only record of a single face verification attempt - never edited or superseded, the
/// same "past attempt is a historical fact" treatment BIO-BIO-004's
/// <c>FingerprintVerificationRecord</c> gives its own records.
///
/// <b>AC03 - "verification evidence is separate from authorization decision".</b> This type
/// carries NO allow/deny field at all, and <see cref="Service.FaceVerificationService.CompareAsync"/>
/// never computes one - <see cref="FaceAuthorizationDecision"/> is a genuinely separate type,
/// produced by the genuinely separate, pure <see cref="FaceAuthorizationPolicy.Decide"/> function
/// FROM a piece of evidence, never folded into the evidence-producing step itself. This lets a
/// caller re-decide the SAME durable evidence under a changed policy later without ever touching
/// the biometric record that produced it.
///
/// <see cref="Score"/>/<see cref="MatcherId"/> are null exactly when
/// <see cref="ComparisonOutcome"/> is <see cref="FaceComparisonOutcome.AcquisitionFailed"/> or
/// <see cref="FaceComparisonOutcome.ReferenceStale"/> - in both cases no comparison ever ran.
/// <see cref="QualityFaceCount"/>/<see cref="QualityApproved"/> are always populated (AC01's
/// "quality ... are recorded" - the live capture's quality is assessed and attached to every
/// attempt, whether or not extraction/comparison went on to succeed). <see cref="ProviderId"/>/
/// <see cref="ProviderVersion"/> name the CLAIMED IDENTITY'S reference's own provider/model
/// version (AC01's "model version"), the same choice BIO-BIO-004 documents for its own record.
///
/// <b>AC04/BIO-BIO-005-QA-01.</b> <see cref="CurrentPhotoMasterVersion"/> is always populated
/// too (the claimed identity's CURRENT photo master version at the moment of this attempt, or
/// null if none exists) - recorded on every attempt, not only a stale one, so a reviewer can see
/// exactly what <see cref="SourcePhotoMasterVersion"/> was compared against without re-deriving
/// it from an external system days later.
/// </summary>
public sealed record FaceVerificationEvidence
{
    public required string Id { get; init; }
    public required string TenantId { get; init; }
    public required string PersonId { get; init; }
    public required string ReferenceId { get; init; }
    public required int SourcePhotoMasterVersion { get; init; }
    public int? CurrentPhotoMasterVersion { get; init; }
    public required FaceComparisonOutcome ComparisonOutcome { get; init; }
    public double? Score { get; init; }
    public required double ThresholdUsed { get; init; }
    public string? MatcherId { get; init; }
    public required string ProviderId { get; init; }
    public required string ProviderVersion { get; init; }
    public required int QualityFaceCount { get; init; }
    public required bool QualityApproved { get; init; }
    public required string OccurredAtUtc { get; init; }
}
