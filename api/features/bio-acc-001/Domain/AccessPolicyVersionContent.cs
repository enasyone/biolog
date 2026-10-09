namespace Biolog.Features.AccessPolicy.Domain;

/// <summary>
/// One immutable version of a policy's rule set and workflow state (BIO-ACC-001-AC02). A
/// downstream caller (<see cref="PolicyEvaluation.PolicyEvaluator"/>) trusts
/// <see cref="Status"/>==<see cref="AccessPolicyStatus.Published"/> to mean this version genuinely
/// passed <see cref="AccessPolicy.Publish"/>'s maker-checker check (approver != author) - a public
/// <c>init</c> would let a caller launder a still-Draft version into one that claims to be
/// published without that workflow ever running. Every property is <c>internal init</c> with an
/// explicit <c>internal</c> constructor, mirroring BIO-DOC-005's <c>ParserVersionContent</c>.
/// </summary>
public sealed record AccessPolicyVersionContent
{
    public int VersionNumber { get; internal init; }
    public IReadOnlyList<PolicyRule> Rules { get; internal init; } = [];
    public AccessPolicyStatus Status { get; internal init; }
    public string AuthorActorId { get; internal init; } = null!;
    public string? PublishedByActorId { get; internal init; }
    public string CreatedAt { get; internal init; } = null!;
    public string? PublishedAt { get; internal init; }

    internal AccessPolicyVersionContent(
        int versionNumber,
        IReadOnlyList<PolicyRule> rules,
        AccessPolicyStatus status,
        string authorActorId,
        string? publishedByActorId,
        string createdAt,
        string? publishedAt)
    {
        VersionNumber = versionNumber;
        Rules = rules;
        Status = status;
        AuthorActorId = authorActorId;
        PublishedByActorId = publishedByActorId;
        CreatedAt = createdAt;
        PublishedAt = publishedAt;
    }
}
