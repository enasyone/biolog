using System.Text;
using Biolog.Features.DocumentTemplateEditor.Audit;
using Biolog.Features.DocumentTemplateEditor.Domain;
using Biolog.Features.DocumentTemplateEditor.Tests.Fixtures;
using Biolog.Features.OcrEngine.Model;
using Xunit;

namespace Biolog.Features.DocumentTemplateEditor.Tests;

public class DocumentTemplateDraftServiceTests
{
    private static DocumentTemplateDraftService NewService(string? tenantId, out InMemoryDocumentTemplateDraftAuditSink auditSink, out InMemoryDocumentTemplateDraftRepository repository) =>
        NewService(tenantId, out auditSink, out repository, out _);

    private static DocumentTemplateDraftService NewService(
        string? tenantId, out InMemoryDocumentTemplateDraftAuditSink auditSink, out InMemoryDocumentTemplateDraftRepository repository, out FakePermissionGateway permissionGateway)
    {
        auditSink = new InMemoryDocumentTemplateDraftAuditSink();
        repository = new InMemoryDocumentTemplateDraftRepository();
        permissionGateway = new FakePermissionGateway();
        return new DocumentTemplateDraftService(new FakeCurrentTenantProvider(tenantId), repository, auditSink, new DocumentTemplateDraftLockRegistry(), permissionGateway);
    }

    [Fact]
    public async Task CreateDraftAsync_throws_tenant_context_missing_when_no_tenant_is_resolved()
    {
        var service = NewService(tenantId: null, out _, out _);

        var error = await Assert.ThrowsAsync<DocumentTemplateDraftError>(() =>
            service.CreateDraftAsync("A Template", TestDrafts.Actor, "evt_00000000-0000-7000-8000-000000000000"));

        Assert.Equal("tenant_context_missing", error.Code);
    }

    [Fact]
    public async Task CreateDraftAsync_scopes_the_new_draft_to_the_resolved_tenant_and_audits_before_persisting()
    {
        var service = NewService(TestDrafts.TenantA, out var auditSink, out _);

        var draft = await service.CreateDraftAsync("A Template", TestDrafts.Actor, "evt_00000000-0000-7000-8000-000000000001");

        Assert.Equal(TestDrafts.TenantA, draft.TenantId);
        var events = auditSink.EventsForTenant(TestDrafts.TenantA);
        Assert.Single(events);
        Assert.Equal("document_template_draft.create", events[0].Action);
        Assert.Equal(AuditOutcome.Allowed, events[0].Outcome);
    }

    /// <summary>ZB-DOC011-03: serviceB's own <see cref="FakePermissionGateway"/> grants everything, so this proves tenant scoping alone - not a permission denial - is what keeps a fully-privileged actor in the wrong tenant from reading another tenant's draft.</summary>
    [Fact]
    public async Task Two_tenants_drafts_are_fully_isolated_from_each_other()
    {
        var serviceA = NewService(TestDrafts.TenantA, out _, out var sharedRepository);
        var serviceB = new DocumentTemplateDraftService(
            new FakeCurrentTenantProvider(TestDrafts.TenantB), sharedRepository, new InMemoryDocumentTemplateDraftAuditSink(),
            new DocumentTemplateDraftLockRegistry(), new FakePermissionGateway());

        var draftA = await serviceA.CreateDraftAsync("Tenant A's Template", TestDrafts.Actor, "evt_00000000-0000-7000-8000-000000000002");

        await Assert.ThrowsAsync<DocumentTemplateDraftError>(() => serviceB.GetVersionAsync(draftA.TemplateId, 1, TestDrafts.Actor.Id));
        Assert.Empty(await serviceB.ListVersionsAsync(draftA.TemplateId, TestDrafts.Actor.Id));
        Assert.Null(await serviceB.GetPublishedAsync(draftA.TemplateId, TestDrafts.Actor.Id));
    }

    [Fact]
    public async Task AttachReferenceLayoutAsync_computes_a_real_content_hash_that_differs_for_different_content()
    {
        var service = NewService(TestDrafts.TenantA, out _, out _);
        var draft = await service.CreateDraftAsync("A Template", TestDrafts.Actor, "evt_00000000-0000-7000-8000-000000000003");

        var withLayout1 = await service.AttachReferenceLayoutAsync(
            draft.TemplateId, 1, 400, 200, Encoding.UTF8.GetBytes("synthetic-content-one"), draft.Revision, TestDrafts.Actor, "evt_00000000-0000-7000-8000-000000000004");

        var withLayout2 = await service.AttachReferenceLayoutAsync(
            draft.TemplateId, 1, 400, 200, Encoding.UTF8.GetBytes("synthetic-content-two"), withLayout1.Revision, TestDrafts.Actor, "evt_00000000-0000-7000-8000-000000000005");

        Assert.NotEqual(withLayout1.ReferenceLayout!.ContentHash, withLayout2.ReferenceLayout!.ContentHash);
        Assert.Equal(64, withLayout2.ReferenceLayout.ContentHash.Length); // SHA-256 as lowercase hex
    }

    [Fact]
    public async Task A_stale_expected_revision_throws_a_concurrency_conflict()
    {
        var service = NewService(TestDrafts.TenantA, out _, out _);
        var draft = await service.CreateDraftAsync("A Template", TestDrafts.Actor, "evt_00000000-0000-7000-8000-000000000006");

        var error = await Assert.ThrowsAsync<DocumentTemplateDraftError>(() =>
            service.AttachReferenceLayoutAsync(draft.TemplateId, 1, 400, 200, [1, 2, 3], expectedRevision: 999, TestDrafts.Actor, "evt_00000000-0000-7000-8000-000000000007"));

        Assert.Equal("document_template_draft_concurrency_conflict", error.Code);
    }

    [Fact]
    public async Task A_lost_concurrency_race_records_a_Denied_audit_event_never_an_Allowed_one()
    {
        var service = NewService(TestDrafts.TenantA, out var auditSink, out _);
        var draft = await service.CreateDraftAsync("A Template", TestDrafts.Actor, "evt_00000000-0000-7000-8000-000000000020");

        await Assert.ThrowsAsync<DocumentTemplateDraftError>(() =>
            service.AttachReferenceLayoutAsync(draft.TemplateId, 1, 400, 200, [1, 2, 3], expectedRevision: 999, TestDrafts.Actor, "evt_00000000-0000-7000-8000-000000000021"));

        var events = auditSink.EventsForTenant(TestDrafts.TenantA);
        var attachEvent = Assert.Single(events, e => e.Action == "document_template_draft.attach_reference_layout");

        Assert.Equal(AuditOutcome.Denied, attachEvent.Outcome);
        Assert.Equal("document_template_draft_concurrency_conflict", attachEvent.ReasonCode);
    }

    [Fact]
    public async Task AC03_RunCorpusAsync_actually_runs_the_real_OCR_engine_and_advances_to_Approval_on_a_genuine_pass()
    {
        var service = NewService(TestDrafts.TenantA, out _, out _);
        var draft = await service.CreateDraftAsync("A Template", TestDrafts.Actor, "evt_00000000-0000-7000-8000-000000000008");
        var withLayout = await service.AttachReferenceLayoutAsync(draft.TemplateId, 1, 400, 200, [1], draft.Revision, TestDrafts.Actor, "evt_00000000-0000-7000-8000-000000000009");
        var withRegions = await service.DefineRegionsAsync(
            draft.TemplateId, 1, [TestDrafts.SurnameField()], [], withLayout.Revision, TestDrafts.Actor, "evt_00000000-0000-7000-8000-00000000000a");
        var inTest = await service.SubmitForTestAsync(draft.TemplateId, 1, withRegions.Revision, TestDrafts.Actor, "evt_00000000-0000-7000-8000-00000000000b");

        var samples = new List<DocumentTemplateCorpusSample>
        {
            new("sample-1", new Dictionary<string, string> { ["SURNAME"] = "OKORO" }),
        };

        var afterRun = await service.RunCorpusAsync(draft.TemplateId, 1, samples, inTest.Revision, TestDrafts.Actor, "evt_00000000-0000-7000-8000-00000000000c");

        Assert.Equal(DocumentTemplateDraftStatus.Approval, afterRun.Status);
        Assert.True(afterRun.LastCorpusRun!.Passed);
    }

    [Fact]
    public async Task AC03_RunCorpusAsync_genuinely_fails_a_corrupted_sample_and_returns_to_Draft()
    {
        var service = NewService(TestDrafts.TenantA, out _, out _);
        var draft = await service.CreateDraftAsync("A Template", TestDrafts.Actor, "evt_00000000-0000-7000-8000-00000000000d");
        var withLayout = await service.AttachReferenceLayoutAsync(draft.TemplateId, 1, 400, 200, [1], draft.Revision, TestDrafts.Actor, "evt_00000000-0000-7000-8000-00000000000e");
        var withRegions = await service.DefineRegionsAsync(
            draft.TemplateId, 1, [TestDrafts.SurnameField()], [], withLayout.Revision, TestDrafts.Actor, "evt_00000000-0000-7000-8000-00000000000f");
        var inTest = await service.SubmitForTestAsync(draft.TemplateId, 1, withRegions.Revision, TestDrafts.Actor, "evt_00000000-0000-7000-8000-000000000010");

        var samples = new List<DocumentTemplateCorpusSample>
        {
            new("sample-1", new Dictionary<string, string> { ["SURNAME"] = "OKORO" }, CorruptFieldNames: new HashSet<string> { "SURNAME" }),
        };

        var afterRun = await service.RunCorpusAsync(draft.TemplateId, 1, samples, inTest.Revision, TestDrafts.Actor, "evt_00000000-0000-7000-8000-000000000011");

        Assert.Equal(DocumentTemplateDraftStatus.Draft, afterRun.Status);
        Assert.False(afterRun.LastCorpusRun!.Passed);
        Assert.Single(afterRun.LastCorpusRun.FailureReasons);
    }

    [Fact]
    public async Task AC03_RunCorpusAsync_refuses_an_empty_corpus()
    {
        var service = NewService(TestDrafts.TenantA, out _, out _);
        var draft = await service.CreateDraftAsync("A Template", TestDrafts.Actor, "evt_00000000-0000-7000-8000-000000000012");
        var withLayout = await service.AttachReferenceLayoutAsync(draft.TemplateId, 1, 400, 200, [1], draft.Revision, TestDrafts.Actor, "evt_00000000-0000-7000-8000-000000000013");
        var withRegions = await service.DefineRegionsAsync(
            draft.TemplateId, 1, [TestDrafts.SurnameField()], [], withLayout.Revision, TestDrafts.Actor, "evt_00000000-0000-7000-8000-000000000014");
        var inTest = await service.SubmitForTestAsync(draft.TemplateId, 1, withRegions.Revision, TestDrafts.Actor, "evt_00000000-0000-7000-8000-000000000015");

        var error = await Assert.ThrowsAsync<DocumentTemplateDraftError>(() =>
            service.RunCorpusAsync(draft.TemplateId, 1, [], inTest.Revision, TestDrafts.Actor, "evt_00000000-0000-7000-8000-000000000016"));

        Assert.Equal("document_template_draft_empty_corpus", error.Code);
    }

    [Fact]
    public async Task GetPublished_returns_null_rather_than_throwing_when_no_version_is_published()
    {
        var service = NewService(TestDrafts.TenantA, out _, out _);
        await service.CreateDraftAsync("A Template", TestDrafts.Actor, "evt_00000000-0000-7000-8000-000000000017");

        Assert.Null(await service.GetPublishedAsync("whatever-template-id", TestDrafts.Actor.Id));
    }

    [Fact]
    public async Task AC05_full_lifecycle_end_to_end_reaches_Published_and_a_next_version_can_then_be_drafted()
    {
        var service = NewService(TestDrafts.TenantA, out _, out _);
        var draft = await service.CreateDraftAsync("A Template", TestDrafts.Actor, "evt_00000000-0000-7000-8000-000000000018");
        var withLayout = await service.AttachReferenceLayoutAsync(draft.TemplateId, 1, 400, 200, [1], draft.Revision, TestDrafts.Actor, "evt_00000000-0000-7000-8000-000000000019");
        var withRegions = await service.DefineRegionsAsync(
            draft.TemplateId, 1, [TestDrafts.SurnameField()], [], withLayout.Revision, TestDrafts.Actor, "evt_00000000-0000-7000-8000-00000000001a");
        var inTest = await service.SubmitForTestAsync(draft.TemplateId, 1, withRegions.Revision, TestDrafts.Actor, "evt_00000000-0000-7000-8000-00000000001b");
        var inApproval = await service.RunCorpusAsync(
            draft.TemplateId, 1, [new DocumentTemplateCorpusSample("s1", new Dictionary<string, string> { ["SURNAME"] = "OKORO" })],
            inTest.Revision, TestDrafts.Actor, "evt_00000000-0000-7000-8000-00000000001c");
        var published = await service.PublishAsync(draft.TemplateId, 1, inApproval.Revision, TestDrafts.Actor, "evt_00000000-0000-7000-8000-00000000001d");

        Assert.Equal(DocumentTemplateDraftStatus.Published, published.Status);
        Assert.Equal(published, await service.GetPublishedAsync(draft.TemplateId, TestDrafts.Actor.Id));

        var next = await service.CreateNextVersionAsync(draft.TemplateId, 1, TestDrafts.Actor, "evt_00000000-0000-7000-8000-00000000001e");
        Assert.Equal(2, next.Version);
        Assert.Equal(DocumentTemplateDraftStatus.Draft, next.Status);
    }

    [Fact]
    public async Task ZB_DOC011_01_a_mutation_without_the_required_permission_is_refused()
    {
        var service = NewService(TestDrafts.TenantA, out _, out _, out var permissionGateway);
        var draft = await service.CreateDraftAsync("A Template", TestDrafts.Actor, "evt_00000000-0000-7000-8000-000000000022");
        permissionGateway.Deny(DocumentTemplateDraftService.ManagePermission);

        var error = await Assert.ThrowsAsync<DocumentTemplateDraftError>(() =>
            service.AttachReferenceLayoutAsync(draft.TemplateId, 1, 400, 200, [1, 2, 3], draft.Revision, TestDrafts.Actor, "evt_00000000-0000-7000-8000-000000000023"));

        Assert.Equal("permission_required", error.Code);
    }

    [Fact]
    public async Task ZB_DOC011_01_publishing_requires_its_own_distinct_permission_from_every_other_mutation()
    {
        var service = NewService(TestDrafts.TenantA, out _, out _, out var permissionGateway);
        var draft = await service.CreateDraftAsync("A Template", TestDrafts.Actor, "evt_00000000-0000-7000-8000-000000000024");
        var withLayout = await service.AttachReferenceLayoutAsync(draft.TemplateId, 1, 400, 200, [1], draft.Revision, TestDrafts.Actor, "evt_00000000-0000-7000-8000-000000000025");
        var withRegions = await service.DefineRegionsAsync(
            draft.TemplateId, 1, [TestDrafts.SurnameField()], [], withLayout.Revision, TestDrafts.Actor, "evt_00000000-0000-7000-8000-000000000026");
        var inTest = await service.SubmitForTestAsync(draft.TemplateId, 1, withRegions.Revision, TestDrafts.Actor, "evt_00000000-0000-7000-8000-000000000027");
        var inApproval = await service.RunCorpusAsync(
            draft.TemplateId, 1, [new DocumentTemplateCorpusSample("s1", new Dictionary<string, string> { ["SURNAME"] = "OKORO" })],
            inTest.Revision, TestDrafts.Actor, "evt_00000000-0000-7000-8000-000000000028");

        // The actor still holds ManagePermission (every step above succeeded with it) but NOT PublishPermission.
        permissionGateway.Deny(DocumentTemplateDraftService.PublishPermission);

        var error = await Assert.ThrowsAsync<DocumentTemplateDraftError>(() =>
            service.PublishAsync(draft.TemplateId, 1, inApproval.Revision, TestDrafts.Actor, "evt_00000000-0000-7000-8000-000000000029"));

        Assert.Equal("permission_required", error.Code);
    }

    [Fact]
    public async Task ZB_DOC011_02_an_ambiguous_write_throw_is_resolved_via_ExistsAsync_and_the_mutation_still_succeeds()
    {
        var inner = new InMemoryDocumentTemplateDraftAuditSink();
        var service = new DocumentTemplateDraftService(
            new FakeCurrentTenantProvider(TestDrafts.TenantA), new InMemoryDocumentTemplateDraftRepository(), new Fixtures.AmbiguousWriteAuditSink(inner),
            new DocumentTemplateDraftLockRegistry(), new FakePermissionGateway());

        var draft = await service.CreateDraftAsync("A Template", TestDrafts.Actor, "evt_00000000-0000-7000-8000-00000000002a");

        Assert.Equal(TestDrafts.TenantA, draft.TenantId);
        Assert.Single(inner.EventsForTenant(TestDrafts.TenantA));
    }

    [Fact]
    public async Task ZB_DOC011_02_a_genuine_total_audit_failure_refuses_the_mutation_and_leaves_no_persisted_row()
    {
        var repository = new InMemoryDocumentTemplateDraftRepository();
        var service = new DocumentTemplateDraftService(
            new FakeCurrentTenantProvider(TestDrafts.TenantA), repository, new Fixtures.AlwaysFailingAuditSink(),
            new DocumentTemplateDraftLockRegistry(), new FakePermissionGateway());

        var error = await Assert.ThrowsAsync<DocumentTemplateDraftError>(() =>
            service.CreateDraftAsync("A Template", TestDrafts.Actor, "evt_00000000-0000-7000-8000-00000000002b"));

        Assert.Equal("audit_unavailable", error.Code);
        Assert.Empty(repository.ListVersions(TestDrafts.TenantA, "whatever"));
    }

    [Fact]
    public async Task ZB_DOC011_02_two_concurrent_CreateNextVersionAsync_calls_never_collide_on_the_same_version_number()
    {
        var service = NewService(TestDrafts.TenantA, out _, out _);
        var draft = await service.CreateDraftAsync("A Template", TestDrafts.Actor, "evt_00000000-0000-7000-8000-00000000002c");
        var withLayout = await service.AttachReferenceLayoutAsync(draft.TemplateId, 1, 400, 200, [1], draft.Revision, TestDrafts.Actor, "evt_00000000-0000-7000-8000-00000000002d");
        var withRegions = await service.DefineRegionsAsync(
            draft.TemplateId, 1, [TestDrafts.SurnameField()], [], withLayout.Revision, TestDrafts.Actor, "evt_00000000-0000-7000-8000-00000000002e");
        var inTest = await service.SubmitForTestAsync(draft.TemplateId, 1, withRegions.Revision, TestDrafts.Actor, "evt_00000000-0000-7000-8000-00000000002f");
        var inApproval = await service.RunCorpusAsync(
            draft.TemplateId, 1, [new DocumentTemplateCorpusSample("s1", new Dictionary<string, string> { ["SURNAME"] = "OKORO" })],
            inTest.Revision, TestDrafts.Actor, "evt_00000000-0000-7000-8000-000000000030");
        await service.PublishAsync(draft.TemplateId, 1, inApproval.Revision, TestDrafts.Actor, "evt_00000000-0000-7000-8000-000000000031");

        var results = await Task.WhenAll(
            service.CreateNextVersionAsync(draft.TemplateId, 1, TestDrafts.Actor, "evt_00000000-0000-7000-8000-000000000032"),
            service.CreateNextVersionAsync(draft.TemplateId, 1, TestDrafts.Actor, "evt_00000000-0000-7000-8000-000000000033"));

        Assert.Equal([2, 3], results.Select(r => r.Version).OrderBy(v => v));
    }

    [Fact]
    public async Task ZB_DOC011_03_an_actor_without_read_manage_or_publish_permission_cannot_read_any_draft_content()
    {
        var service = NewService(TestDrafts.TenantA, out _, out _, out var permissionGateway);
        var draft = await service.CreateDraftAsync("A Template", TestDrafts.Actor, "evt_00000000-0000-7000-8000-000000000034");

        permissionGateway.Deny(DocumentTemplateDraftService.ReadPermission);
        permissionGateway.Deny(DocumentTemplateDraftService.ManagePermission);
        permissionGateway.Deny(DocumentTemplateDraftService.PublishPermission);

        var listError = await Assert.ThrowsAsync<DocumentTemplateDraftError>(() => service.ListVersionsAsync(draft.TemplateId, TestDrafts.Actor.Id));
        var getError = await Assert.ThrowsAsync<DocumentTemplateDraftError>(() => service.GetVersionAsync(draft.TemplateId, 1, TestDrafts.Actor.Id));
        var publishedError = await Assert.ThrowsAsync<DocumentTemplateDraftError>(() => service.GetPublishedAsync(draft.TemplateId, TestDrafts.Actor.Id));

        Assert.Equal("permission_required", listError.Code);
        Assert.Equal("permission_required", getError.Code);
        Assert.Equal("permission_required", publishedError.Code);
    }

    /// <summary>ZB-DOC011-03: a caller who can manage (or publish) a lineage can obviously also view it - <see cref="DocumentTemplateDraftService.ReadPermission"/> is not the only way to pass a read check.</summary>
    [Fact]
    public async Task ZB_DOC011_03_an_actor_who_only_holds_manage_permission_can_still_read_what_they_manage()
    {
        var service = NewService(TestDrafts.TenantA, out _, out _, out var permissionGateway);
        var draft = await service.CreateDraftAsync("A Template", TestDrafts.Actor, "evt_00000000-0000-7000-8000-000000000035");

        permissionGateway.Deny(DocumentTemplateDraftService.ReadPermission);
        permissionGateway.Deny(DocumentTemplateDraftService.PublishPermission);

        var versions = await service.ListVersionsAsync(draft.TemplateId, TestDrafts.Actor.Id);
        Assert.Single(versions);
    }
}
