namespace Biolog.Features.DocumentTemplateEditor.Domain;

/// <summary>BIO-DOC-011-AC02: "Template has draft/test/approval/publish lifecycle." One-way except Test's own corpus-fail path back to Draft (<see cref="DocumentTemplateDraft.RecordCorpusRun"/>) - the same "fail returns to Draft, pass advances" shape BIO-CRD-008's own <c>CardTemplateStatus</c> uses for its Validation stage.</summary>
public enum DocumentTemplateDraftStatus
{
    Draft,
    Test,
    Approval,
    Published,
}

public static class DocumentTemplateDraftStatusExtensions
{
    public static string ToWireString(this DocumentTemplateDraftStatus status) => status switch
    {
        DocumentTemplateDraftStatus.Draft => "draft",
        DocumentTemplateDraftStatus.Test => "test",
        DocumentTemplateDraftStatus.Approval => "approval",
        DocumentTemplateDraftStatus.Published => "published",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "unreachable: exhaustive switch"),
    };

    /// <summary>AC05: once true, every content-bearing mutation on <see cref="DocumentTemplateDraft"/> refuses - the only way to change content from here is <see cref="DocumentTemplateDraft.CreateNextDraftVersion"/>, a brand new version row (ADR-0001 §3.3).</summary>
    public static bool IsImmutable(this DocumentTemplateDraftStatus status) => status is DocumentTemplateDraftStatus.Published;
}
