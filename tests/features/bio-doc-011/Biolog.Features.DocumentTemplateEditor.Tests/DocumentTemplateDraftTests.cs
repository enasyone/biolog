using Biolog.Features.DocumentTemplateEditor.Domain;
using Biolog.Features.DocumentTemplateEditor.Tests.Fixtures;
using Biolog.Features.OcrEngine.Model;
using Xunit;

namespace Biolog.Features.DocumentTemplateEditor.Tests;

public class DocumentTemplateDraftTests
{
    [Fact]
    public void A_new_draft_starts_in_Draft_status_with_no_regions()
    {
        var draft = TestDrafts.NewDraft();

        Assert.Equal(DocumentTemplateDraftStatus.Draft, draft.Status);
        Assert.Empty(draft.FieldRegions);
        Assert.Empty(draft.GraphicRegions);
        Assert.Null(draft.ReferenceLayout);
        Assert.Null(draft.LastCorpusRun);
        Assert.Equal(1, draft.Version);
        Assert.Equal(1, draft.Revision);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-tenant-id")]
    [InlineData("tpl_11111111-1111-7111-8111-111111111111")]
    public void Construction_rejects_a_malformed_tenant_id(string badTenantId)
    {
        Assert.Throws<ArgumentException>(() => TestDrafts.NewDraft(tenantId: badTenantId));
    }

    [Fact]
    public void AC04_PublicReference_always_carries_a_tenant_prefixed_identifier_naming_the_resolved_tenant()
    {
        var draft = TestDrafts.NewDraft(tenantId: TestDrafts.TenantB);

        Assert.StartsWith("tenant:", draft.PublicReference, StringComparison.Ordinal);
        Assert.Contains(TestDrafts.TenantB, draft.PublicReference, StringComparison.Ordinal);
        Assert.Contains(draft.TemplateId, draft.PublicReference, StringComparison.Ordinal);
    }

    [Fact]
    public void AC04_there_is_no_constructor_that_can_produce_a_draft_without_a_well_formed_tenant_id()
    {
        // The compiler-generated record copy constructor (DocumentTemplateDraft original) only ever
        // copies an ALREADY-VALID instance's own TenantId forward via `with` - it can never mint a
        // new tenant id, so it is not a bypass and is excluded here deliberately, not overlooked.
        var domainCtors = typeof(DocumentTemplateDraft)
            .GetConstructors(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
            .Where(c => c.GetParameters() is [{ ParameterType.Name: "String" }, ..])
            .ToList();

        Assert.Single(domainCtors);
        Assert.False(domainCtors[0].IsPublic);

        var parameters = domainCtors[0].GetParameters();
        Assert.Equal("tenantId", parameters[0].Name);
    }

    [Fact]
    public void AttachReferenceLayout_sets_the_layout_and_clears_any_prior_regions()
    {
        var withRegions = TestDrafts.WithLayoutAndRegions();
        Assert.NotEmpty(withRegions.FieldRegions);

        var reattached = withRegions.AttachReferenceLayout(TestDrafts.Layout(800, 600), TestDrafts.Actor, TestDrafts.Now);

        Assert.Equal(800, reattached.ReferenceLayout!.Width);
        Assert.Empty(reattached.FieldRegions);
        Assert.Empty(reattached.GraphicRegions);
    }

    [Fact]
    public void AC01_DefineRegions_refuses_before_a_reference_layout_is_attached()
    {
        var draft = TestDrafts.NewDraft();

        var error = Assert.Throws<DocumentTemplateDraftError>(() =>
            draft.DefineRegions([TestDrafts.SurnameField()], [], TestDrafts.Actor, TestDrafts.Now));

        Assert.Equal("document_template_draft_reference_layout_required", error.Code);
    }

    [Fact]
    public void AC01_DefineRegions_rejects_a_duplicate_field_region_name()
    {
        var draft = TestDrafts.NewDraft().AttachReferenceLayout(TestDrafts.Layout(), TestDrafts.Actor, TestDrafts.Now);
        var duplicate = new OcrTemplateField("SURNAME", OcrFieldType.GivenNames, 100, 10, 5, false);

        var error = Assert.Throws<DocumentTemplateDraftError>(() =>
            draft.DefineRegions([TestDrafts.SurnameField(), duplicate], [], TestDrafts.Actor, TestDrafts.Now));

        Assert.Equal("document_template_draft_duplicate_region_name", error.Code);
    }

    [Fact]
    public void AC01_DefineRegions_rejects_a_duplicate_graphic_region_name()
    {
        var draft = TestDrafts.NewDraft().AttachReferenceLayout(TestDrafts.Layout(), TestDrafts.Actor, TestDrafts.Now);
        var graphic1 = new DocumentTemplateGraphicRegion("Watermark", 10, 10, 20, 20);
        var graphic2 = new DocumentTemplateGraphicRegion("Watermark", 50, 50, 20, 20);

        var error = Assert.Throws<DocumentTemplateDraftError>(() =>
            draft.DefineRegions([], [graphic1, graphic2], TestDrafts.Actor, TestDrafts.Now));

        Assert.Equal("document_template_draft_duplicate_region_name", error.Code);
    }

    [Theory]
    [InlineData(-1, 10)]
    [InlineData(10, -1)]
    public void AC01_DefineRegions_rejects_negative_field_geometry(int left, int top)
    {
        var draft = TestDrafts.NewDraft().AttachReferenceLayout(TestDrafts.Layout(), TestDrafts.Actor, TestDrafts.Now);
        var field = new OcrTemplateField("SURNAME", OcrFieldType.Surname, left, top, 5, false);

        var error = Assert.Throws<DocumentTemplateDraftError>(() =>
            draft.DefineRegions([field], [], TestDrafts.Actor, TestDrafts.Now));

        Assert.Equal("document_template_draft_invalid_region_geometry", error.Code);
    }

    [Fact]
    public void AC01_DefineRegions_rejects_a_field_region_that_does_not_fit_within_the_layout()
    {
        var draft = TestDrafts.NewDraft().AttachReferenceLayout(TestDrafts.Layout(width: 50, height: 50), TestDrafts.Actor, TestDrafts.Now);
        var field = TestDrafts.SurnameField(left: 40, top: 40, characterCount: 10);

        var error = Assert.Throws<DocumentTemplateDraftError>(() =>
            draft.DefineRegions([field], [], TestDrafts.Actor, TestDrafts.Now));

        Assert.Equal("document_template_draft_region_out_of_bounds", error.Code);
    }

    [Fact]
    public void AC01_DefineRegions_rejects_a_graphic_region_that_does_not_fit_within_the_layout()
    {
        var draft = TestDrafts.NewDraft().AttachReferenceLayout(TestDrafts.Layout(width: 50, height: 50), TestDrafts.Actor, TestDrafts.Now);
        var graphic = new DocumentTemplateGraphicRegion("Hologram", 40, 40, 30, 30);

        var error = Assert.Throws<DocumentTemplateDraftError>(() =>
            draft.DefineRegions([], [graphic], TestDrafts.Actor, TestDrafts.Now));

        Assert.Equal("document_template_draft_region_out_of_bounds", error.Code);
    }

    [Fact]
    public void AC01_DefineRegions_accepts_a_well_formed_field_and_graphic_region_set()
    {
        var draft = TestDrafts.NewDraft().AttachReferenceLayout(TestDrafts.Layout(), TestDrafts.Actor, TestDrafts.Now);
        var graphic = new DocumentTemplateGraphicRegion("SecurityHologram", 300, 150, 50, 40);

        var defined = draft.DefineRegions([TestDrafts.SurnameField()], [graphic], TestDrafts.Actor, TestDrafts.Now);

        Assert.Single(defined.FieldRegions);
        Assert.Single(defined.GraphicRegions);
        Assert.Equal("SURNAME", defined.FieldRegions[0].FieldName);
    }

    [Fact]
    public void AC02_SubmitForTest_refuses_without_at_least_one_field_region()
    {
        var draft = TestDrafts.NewDraft().AttachReferenceLayout(TestDrafts.Layout(), TestDrafts.Actor, TestDrafts.Now);

        var error = Assert.Throws<DocumentTemplateDraftError>(() => draft.SubmitForTest(TestDrafts.Actor, TestDrafts.Now));

        Assert.Equal("document_template_draft_no_field_regions", error.Code);
    }

    [Fact]
    public void AC02_SubmitForTest_moves_Draft_to_Test()
    {
        var draft = TestDrafts.WithLayoutAndRegions();

        var inTest = draft.SubmitForTest(TestDrafts.Actor, TestDrafts.Now);

        Assert.Equal(DocumentTemplateDraftStatus.Test, inTest.Status);
    }

    [Fact]
    public void AC02_SubmitForTest_refuses_from_any_status_other_than_Draft()
    {
        var inTest = TestDrafts.InTest();

        var error = Assert.Throws<DocumentTemplateDraftError>(() => inTest.SubmitForTest(TestDrafts.Actor, TestDrafts.Now));

        Assert.Equal("document_template_draft_invalid_transition", error.Code);
    }

    [Fact]
    public void AC03_RecordCorpusRun_moves_Test_to_Approval_when_the_corpus_passed()
    {
        var inTest = TestDrafts.InTest();
        var result = new DocumentTemplateCorpusRunResult(passed: true, sampleCount: 2, failureReasons: [], ranAt: TestDrafts.Now);

        var approved = inTest.RecordCorpusRun(result, TestDrafts.Actor, TestDrafts.Now);

        Assert.Equal(DocumentTemplateDraftStatus.Approval, approved.Status);
        Assert.True(approved.LastCorpusRun!.Passed);
    }

    [Fact]
    public void AC03_RecordCorpusRun_returns_Test_to_Draft_when_the_corpus_failed()
    {
        var inTest = TestDrafts.InTest();
        var result = new DocumentTemplateCorpusRunResult(passed: false, sampleCount: 2, failureReasons: ["s1:SURNAME:expected='A':actual='B'"], ranAt: TestDrafts.Now);

        var backToDraft = inTest.RecordCorpusRun(result, TestDrafts.Actor, TestDrafts.Now);

        Assert.Equal(DocumentTemplateDraftStatus.Draft, backToDraft.Status);
        Assert.False(backToDraft.LastCorpusRun!.Passed);
    }

    [Fact]
    public void AC03_RecordCorpusRun_refuses_outside_Test_status()
    {
        var draft = TestDrafts.NewDraft();
        var result = new DocumentTemplateCorpusRunResult(passed: true, sampleCount: 1, failureReasons: [], ranAt: TestDrafts.Now);

        var error = Assert.Throws<DocumentTemplateDraftError>(() => draft.RecordCorpusRun(result, TestDrafts.Actor, TestDrafts.Now));

        Assert.Equal("document_template_draft_invalid_transition", error.Code);
    }

    [Fact]
    public void AC05_Publish_moves_Approval_to_Published_and_stamps_PublishedAt_PublishedBy()
    {
        var inApproval = TestDrafts.InApproval();

        var published = inApproval.Publish(TestDrafts.Actor, TestDrafts.Now);

        Assert.Equal(DocumentTemplateDraftStatus.Published, published.Status);
        Assert.Equal(TestDrafts.Now, published.PublishedAt);
        Assert.Equal(TestDrafts.Actor, published.PublishedBy);
    }

    [Fact]
    public void AC05_Publish_refuses_outside_Approval_status()
    {
        var draft = TestDrafts.NewDraft();

        var error = Assert.Throws<DocumentTemplateDraftError>(() => draft.Publish(TestDrafts.Actor, TestDrafts.Now));

        Assert.Equal("document_template_draft_invalid_transition", error.Code);
    }

    [Theory]
    [MemberData(nameof(MutatorsThatMustRefuseOncePublished))]
    public void AC05_every_content_mutator_refuses_once_Published(Action<DocumentTemplateDraft> mutate)
    {
        var published = TestDrafts.Published();

        Assert.Throws<DocumentTemplateDraftError>(() => mutate(published));
    }

    public static IEnumerable<object[]> MutatorsThatMustRefuseOncePublished()
    {
        yield return [new Action<DocumentTemplateDraft>(d => d.AttachReferenceLayout(TestDrafts.Layout(), TestDrafts.Actor, TestDrafts.Now))];
        yield return [new Action<DocumentTemplateDraft>(d => d.DefineRegions([TestDrafts.SurnameField()], [], TestDrafts.Actor, TestDrafts.Now))];
        yield return [new Action<DocumentTemplateDraft>(d => d.SubmitForTest(TestDrafts.Actor, TestDrafts.Now))];
        yield return [new Action<DocumentTemplateDraft>(d => d.Publish(TestDrafts.Actor, TestDrafts.Now))];
    }

    [Fact]
    public void AC05_CreateNextDraftVersion_refuses_unless_the_source_is_Published()
    {
        var draft = TestDrafts.NewDraft();

        var error = Assert.Throws<DocumentTemplateDraftError>(() => draft.CreateNextDraftVersion(2, TestDrafts.Actor, TestDrafts.Now));

        Assert.Equal("document_template_draft_invalid_transition", error.Code);
    }

    [Fact]
    public void AC05_CreateNextDraftVersion_starts_a_fresh_Draft_carrying_forward_content_but_not_the_corpus_result()
    {
        var published = TestDrafts.Published();

        var next = published.CreateNextDraftVersion(2, TestDrafts.Actor, TestDrafts.Now);

        Assert.Equal(DocumentTemplateDraftStatus.Draft, next.Status);
        Assert.Equal(2, next.Version);
        Assert.Equal(1, next.Revision);
        Assert.Equal(published.TemplateId, next.TemplateId);
        Assert.Equal(published.TenantId, next.TenantId);
        Assert.Equal(published.FieldRegions.Count, next.FieldRegions.Count);
        Assert.Null(next.LastCorpusRun);
        Assert.Null(next.PublishedAt);
    }

    [Fact]
    public void There_is_no_way_to_edit_a_published_draft_s_content_in_place_other_than_creating_a_next_version()
    {
        var methodNames = typeof(DocumentTemplateDraft).GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)
            .Where(m => m.DeclaringType == typeof(DocumentTemplateDraft))
            .Select(m => m.Name)
            .ToHashSet();

        Assert.DoesNotContain("RemoveFieldRegion", methodNames);
        Assert.DoesNotContain("ReplaceFieldRegion", methodNames);
        Assert.DoesNotContain("UpdateReferenceLayout", methodNames);
        Assert.Contains("CreateNextDraftVersion", methodNames);
    }
}
