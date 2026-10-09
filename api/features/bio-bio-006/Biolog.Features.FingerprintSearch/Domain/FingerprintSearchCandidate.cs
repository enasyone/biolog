namespace Biolog.Features.FingerprintSearch.Domain;

/// <summary>AC01: one ranked candidate from a 1:N search - a person id plus the score the
/// configured <c>IBiometricMatcher</c> produced comparing the live probe against that person's
/// gallery-indexed template. <see cref="PossibleDuplicate"/> is AC03's routing signal: true when
/// <see cref="Score"/> met the tenant's policy-resolved duplicate-review threshold, in which case
/// this candidate is also raised/augmented onto the review queue - see
/// <see cref="Service.FingerprintSearchService"/>'s remarks.</summary>
public sealed record FingerprintSearchCandidate
{
    public required string PersonId { get; init; }
    public required double Score { get; init; }
    public required bool PossibleDuplicate { get; init; }
}
