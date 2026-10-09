namespace Biolog.Features.FaceVerification.Domain;

/// <summary>AC03's "authorization decision" half - produced by <see cref="FaceAuthorizationPolicy.Decide"/>
/// from a <see cref="FaceVerificationEvidence"/>, never persisted alongside it (see that type's
/// remarks for why). Deliberately never carries the raw biometric facts itself (score, threshold,
/// quality) - a caller that needs those reads them from <see cref="EvidenceId"/>'s own evidence
/// record.</summary>
public sealed record FaceAuthorizationDecision
{
    public required string EvidenceId { get; init; }
    public required AccessDecision Decision { get; init; }
    public string? DenyReason { get; init; }
}
