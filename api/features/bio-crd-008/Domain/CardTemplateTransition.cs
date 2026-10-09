using Biolog.Features.CardTemplates.Audit;

namespace Biolog.Features.CardTemplates.Domain;

/// <summary>One recorded lifecycle transition - the story title's own "provenance": who moved this template between which two states, and when. Appended, never edited or removed.</summary>
public sealed record CardTemplateTransition
{
    public required CardTemplateStatus FromStatus { get; init; }
    public required CardTemplateStatus ToStatus { get; init; }
    public required AuditActor Actor { get; init; }
    public required string At { get; init; }
    public string? Note { get; init; }
}
