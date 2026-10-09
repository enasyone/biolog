using Biolog.Features.DocumentTemplateEditor.Audit;
using Biolog.Features.DocumentTemplateEditor.Domain;
using Biolog.Features.DocumentTemplateEditor.Identifiers;
using Biolog.Features.OcrEngine.Model;

namespace Biolog.Features.DocumentTemplateEditor.Tests.Fixtures;

internal static class TestDrafts
{
    public const string TenantA = "ten_11111111-1111-7111-8111-111111111111";
    public const string TenantB = "ten_22222222-2222-7222-8222-222222222222";

    public static readonly AuditActor Actor = new(ActorType.User, "usr_alice");
    public static readonly string Now = UtcInstant.Now();

    public static DocumentTemplateDraft NewDraft(string? tenantId = null, string name = "Legacy ID Card") => new(
        tenantId ?? TenantA, CanonicalId.NewDraftId(), version: 1)
    {
        Revision = 1,
        Status = DocumentTemplateDraftStatus.Draft,
        Name = name,
        CreatedBy = Actor,
        CreatedAt = Now,
        UpdatedAt = Now,
    };

    public static DocumentTemplateReferenceLayout Layout(int width = 400, int height = 200) =>
        new(width, height, "deadbeef", Now);

    /// <summary>One field region ("SURNAME", 10 characters) that fits comfortably inside <see cref="Layout"/>'s default 400x200 canvas.</summary>
    public static OcrTemplateField SurnameField(int left = 10, int top = 10, int characterCount = 10, bool critical = true) =>
        new("SURNAME", OcrFieldType.Surname, left, top, characterCount, critical);

    public static DocumentTemplateDraft WithLayoutAndRegions(DocumentTemplateDraft? draft = null)
    {
        var current = draft ?? NewDraft();
        var withLayout = current.AttachReferenceLayout(Layout(), Actor, Now);
        return withLayout.DefineRegions([SurnameField()], [], Actor, Now);
    }

    public static DocumentTemplateDraft InTest(DocumentTemplateDraft? draft = null) =>
        WithLayoutAndRegions(draft).SubmitForTest(Actor, Now);

    public static DocumentTemplateDraft InApproval(DocumentTemplateDraft? draft = null)
    {
        var inTest = InTest(draft);
        var passResult = new DocumentTemplateCorpusRunResult(passed: true, sampleCount: 1, failureReasons: [], ranAt: Now);
        return inTest.RecordCorpusRun(passResult, Actor, Now);
    }

    public static DocumentTemplateDraft Published(DocumentTemplateDraft? draft = null) =>
        InApproval(draft).Publish(Actor, Now);
}
