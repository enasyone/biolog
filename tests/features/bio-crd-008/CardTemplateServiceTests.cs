using Biolog.Features.CardTemplates.Audit;
using Biolog.Features.CardTemplates.Domain;
using Xunit;

namespace Biolog.Features.CardTemplates.Tests;

public class CardTemplateServiceTests
{
    private static (CardTemplateService Service, ICardTemplateRepository Repository, InMemoryCardTemplateAuditSink AuditSink, FakeSignatureAssetGateway Gateway, FakeCurrentTenantProvider Tenant) Build()
    {
        var tenant = new FakeCurrentTenantProvider(TestData.TenantA);
        var repository = new InMemoryCardTemplateRepository();
        var gateway = new FakeSignatureAssetGateway();
        var auditSink = new InMemoryCardTemplateAuditSink();
        var service = new CardTemplateService(tenant, repository, gateway, auditSink);
        return (service, repository, auditSink, gateway, tenant);
    }

    [Fact]
    public async Task AC03_creating_a_draft_retains_every_snapshot_field_exactly_on_round_trip()
    {
        var (service, _, _, _, _) = Build();
        var snapshot = TestData.MinimalSnapshot();

        var created = await service.CreateDraftAsync("Staff ID v1", snapshot, TestData.Actor, "evt_018f2e5a-4444-7abc-89ab-0123456789ab");

        Assert.Equal(snapshot, created.Snapshot);
        Assert.Equal(CardTemplateStatus.Draft, created.Status);
        Assert.Equal(1, created.Version);
    }

    [Fact]
    public async Task AC03_a_custom_format_without_custom_size_is_a_governed_validation_failure()
    {
        var (service, _, _, _, _) = Build();
        var badSnapshot = TestData.MinimalSnapshot() with { FormatKind = CardFormatKind.Custom, CustomSizeMm = null };

        var error = await Assert.ThrowsAsync<CardTemplateError>(() => service.CreateDraftAsync("Bad", badSnapshot, TestData.Actor, "evt_018f2e5a-4444-7abc-89ab-0123456789ab"));
        Assert.Equal("card_template_custom_size_required", error.Code);
    }

    [Fact]
    public async Task AC03_a_required_signature_rule_must_reference_a_real_existing_signature_asset()
    {
        var (service, _, _, gateway, _) = Build();
        var snapshot = TestData.WithRequiredSignature("lineage-1", 1);

        // gateway knows nothing about lineage-1 yet
        var error = await Assert.ThrowsAsync<CardTemplateError>(() => service.CreateDraftAsync("Needs sig", snapshot, TestData.Actor, "evt_018f2e5a-4444-7abc-89ab-0123456789ab"));
        Assert.Equal("card_template_signature_asset_not_found", error.Code);

        gateway.WithAsset(TestData.TenantA, "lineage-1", 1);
        var created = await service.CreateDraftAsync("Needs sig", snapshot, TestData.Actor, "evt_018f2e5a-4444-7abc-89ab-0123456789ab");
        Assert.True(created.Snapshot.Signature.Required);
    }

    [Fact]
    public async Task tenant_isolation_a_template_created_for_tenant_A_is_invisible_to_tenant_B()
    {
        var (service, repository, _, _, tenant) = Build();
        var created = await service.CreateDraftAsync("A's template", TestData.MinimalSnapshot(), TestData.Actor, "evt_018f2e5a-4444-7abc-89ab-0123456789ab");

        tenant.TenantId = TestData.TenantB;
        var error = Assert.Throws<CardTemplateError>(() => service.GetVersion(created.TemplateId, created.Version));
        Assert.Equal("card_template_not_found", error.Code); // never a confirmed-to-exist 403 - matches BIO-RPT-002's own precedent
        Assert.Equal(404, error.Status);

        Assert.Empty(service.ListLineages()); // tenant B's own listing is empty, not merely filtered client-side
        tenant.TenantId = TestData.TenantA;
        Assert.Single(service.ListLineages());
    }

    [Fact]
    public async Task AC04_getPublished_returns_only_the_currently_published_version_never_a_draft_or_retired_one()
    {
        var (service, _, _, _, _) = Build();
        var draft = await service.CreateDraftAsync("v1", TestData.MinimalSnapshot(), TestData.Actor, "evt_018f2e5a-4444-7abc-89ab-0123456789ab");

        var notYetPublished = Assert.Throws<CardTemplateError>(() => service.GetPublished(draft.TemplateId));
        Assert.Equal("card_template_not_published", notYetPublished.Code);

        var submitted = await service.SubmitForValidationAsync(draft.TemplateId, draft.Version, draft.Revision, TestData.Actor, "evt_018f2e5a-4444-7abc-89ab-0123456789ab");
        var approved = await service.RecordValidationAsync(draft.TemplateId, draft.Version, true, null, submitted.Revision, TestData.Actor, "evt_018f2e5a-4444-7abc-89ab-0123456789ab");
        var published = await service.PublishAsync(draft.TemplateId, draft.Version, approved.Revision, TestData.Actor, "evt_018f2e5a-4444-7abc-89ab-0123456789ab");

        var resolved = service.GetPublished(draft.TemplateId);
        Assert.Equal(published.Version, resolved.Version);
        Assert.Equal(CardTemplateStatus.Published, resolved.Status);
    }

    [Fact]
    public async Task AC02_publishing_a_new_version_auto_retires_the_previously_published_one_only_one_published_row_per_lineage()
    {
        var (service, _, _, _, _) = Build();
        var v1 = await service.CreateDraftAsync("v1", TestData.MinimalSnapshot(), TestData.Actor, "evt_018f2e5a-4444-7abc-89ab-0123456789ab");
        v1 = await service.SubmitForValidationAsync(v1.TemplateId, v1.Version, v1.Revision, TestData.Actor, "e");
        v1 = await service.RecordValidationAsync(v1.TemplateId, v1.Version, true, null, v1.Revision, TestData.Actor, "e");
        v1 = await service.PublishAsync(v1.TemplateId, v1.Version, v1.Revision, TestData.Actor, "e");

        var v2 = await service.CreateNextVersionAsync(v1.TemplateId, v1.Version, null, TestData.Actor, "e");
        v2 = await service.SubmitForValidationAsync(v2.TemplateId, v2.Version, v2.Revision, TestData.Actor, "e");
        v2 = await service.RecordValidationAsync(v2.TemplateId, v2.Version, true, null, v2.Revision, TestData.Actor, "e");
        v2 = await service.PublishAsync(v2.TemplateId, v2.Version, v2.Revision, TestData.Actor, "e");

        var stillV1 = service.GetVersion(v1.TemplateId, v1.Version);
        Assert.Equal(CardTemplateStatus.Retired, stillV1.Status);

        var published = service.GetPublished(v1.TemplateId);
        Assert.Equal(v2.Version, published.Version);
    }

    [Fact]
    public async Task a_stale_expected_revision_is_a_concurrency_conflict_not_a_silent_overwrite()
    {
        var (service, _, _, _, _) = Build();
        var draft = await service.CreateDraftAsync("v1", TestData.MinimalSnapshot(), TestData.Actor, "evt_018f2e5a-4444-7abc-89ab-0123456789ab");

        await service.SubmitForValidationAsync(draft.TemplateId, draft.Version, draft.Revision, TestData.Actor, "e"); // bumps revision once

        var error = await Assert.ThrowsAsync<CardTemplateError>(() =>
            service.SubmitForValidationAsync(draft.TemplateId, draft.Version, draft.Revision, TestData.Actor, "e")); // stale revision, already consumed
        Assert.Equal("card_template_invalid_transition", error.Code); // now Validation, not Draft - a stale caller sees the state error first
    }

    [Fact]
    public async Task every_mutation_writes_a_provenance_audit_event_before_the_caller_sees_a_result()
    {
        var (service, _, auditSink, _, _) = Build();
        var draft = await service.CreateDraftAsync("v1", TestData.MinimalSnapshot(), TestData.Actor, "evt_018f2e5a-4444-7abc-89ab-0123456789ab");
        await service.SubmitForValidationAsync(draft.TemplateId, draft.Version, draft.Revision, TestData.Actor, "evt_018f2e5a-5555-7abc-89ab-0123456789ab");

        var events = auditSink.EventsForTenant(TestData.TenantA);
        Assert.Contains(events, e => e.Action == "card_template.create_draft" && e.Outcome == AuditOutcome.Allowed);
        Assert.Contains(events, e => e.Action == "card_template.submit_for_validation" && e.Outcome == AuditOutcome.Allowed);
    }

    [Fact]
    public void no_tenant_context_is_a_governed_error_not_a_null_reference()
    {
        var (service, _, _, _, tenant) = Build();
        tenant.TenantId = null;
        var error = Assert.Throws<CardTemplateError>(() => service.ListLineages());
        Assert.Equal("tenant_context_missing", error.Code);
    }
}
