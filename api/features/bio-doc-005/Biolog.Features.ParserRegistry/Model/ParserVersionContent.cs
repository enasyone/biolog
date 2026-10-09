namespace Biolog.Features.ParserRegistry.Model;

/// <summary>
/// One immutable version of a parser's content and workflow state (BIO-DOC-005-AC02/AC03/AC04).
/// A downstream caller trusts <see cref="Status"/>`==Published` to mean this version genuinely
/// passed testing and a maker-checker approval (BIO-DOC-005-AC02) - a public <c>init</c> would let
/// a caller launder a still-Draft version into one that claims to be published, without the
/// workflow's own <see cref="ParserRecord.Publish"/> ever running. Every property is
/// <c>internal init</c> with an explicit <c>internal</c> constructor, the same reasoning as every
/// other trust-bearing result type in this codebase's document-intelligence stories.
/// </summary>
public sealed record ParserVersionContent
{
    public int VersionNumber { get; internal init; }
    public ParserApplicability Applicability { get; internal init; } = null!;
    public IReadOnlyList<ParserFieldMapping> FieldMappings { get; internal init; } = [];
    public IReadOnlyList<ParserValidationRule> ValidationRules { get; internal init; } = [];
    public ParserStatus Status { get; internal init; }
    public string AuthorActorId { get; internal init; } = null!;
    public string? TestedByActorId { get; internal init; }
    public string? PublishedByActorId { get; internal init; }
    public string CreatedAt { get; internal init; } = null!;
    public string? PublishedAt { get; internal init; }

    internal ParserVersionContent(
        int versionNumber,
        ParserApplicability applicability,
        IReadOnlyList<ParserFieldMapping> fieldMappings,
        IReadOnlyList<ParserValidationRule> validationRules,
        ParserStatus status,
        string authorActorId,
        string? testedByActorId,
        string? publishedByActorId,
        string createdAt,
        string? publishedAt)
    {
        VersionNumber = versionNumber;
        Applicability = applicability;
        FieldMappings = fieldMappings;
        ValidationRules = validationRules;
        Status = status;
        AuthorActorId = authorActorId;
        TestedByActorId = testedByActorId;
        PublishedByActorId = publishedByActorId;
        CreatedAt = createdAt;
        PublishedAt = publishedAt;
    }
}
