using Biolog.Features.DocumentTemplateEditor.Audit;

namespace Biolog.Features.DocumentTemplateEditor.Domain;

/// <summary>One recorded lifecycle transition - who moved this draft between which two states, and when. Appended, never edited or removed. Mirrors BIO-CRD-008's own <c>CardTemplateTransition</c>.</summary>
public sealed record DocumentTemplateDraftTransition
{
    public required DocumentTemplateDraftStatus FromStatus { get; init; }
    public required DocumentTemplateDraftStatus ToStatus { get; init; }
    public required AuditActor Actor { get; init; }
    public required string At { get; init; }
    public string? Note { get; init; }
}
