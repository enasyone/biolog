namespace Biolog.Features.FaceSearch.Domain;

/// <summary>AC01: one ranked candidate from a 1:N face search - a person id plus the similarity
/// score the configured <c>IBiometricMatcher</c> produced comparing the live probe against that
/// person's gallery-indexed template. Deliberately carries no allow/deny or authorization field
/// of any kind (AC03: "no automatic authorization is granted from search alone") - see
/// <see cref="Service.FaceSearchService"/>'s remarks.</summary>
public sealed record FaceSearchCandidate
{
    public required string PersonId { get; init; }
    public required double Score { get; init; }
}
