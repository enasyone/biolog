namespace Biolog.Features.Liveness.Domain;

/// <summary>
/// BIO-BIO-008-AC01/AC02. Deliberately shares NO member, name or type with any face-MATCH
/// outcome anywhere in this codebase (e.g. BIO-BIO-005's own <c>MatchOutcome</c>): a liveness
/// check answers "is this a live presentation of a subject", never "does this subject match an
/// enrolled reference" - the two questions are answered by entirely separate types on purpose
/// (AC02, "failure is distinct from face mismatch").
/// </summary>
internal enum LivenessStatus
{
    /// <summary>The presentation is assessed as a live subject.</summary>
    Live,

    /// <summary>A presentation-attack pattern (replay/screen/photo) was recognised - a
    /// meaningful, routine liveness OUTCOME, not an integration error.</summary>
    SpoofDetected,

    /// <summary>The provider could not reach a confident verdict either way (e.g. a low-quality
    /// or ambiguous capture) - distinct from both <see cref="Live"/> and
    /// <see cref="SpoofDetected"/>, and still a routine outcome, not an exception.</summary>
    Inconclusive,
}
