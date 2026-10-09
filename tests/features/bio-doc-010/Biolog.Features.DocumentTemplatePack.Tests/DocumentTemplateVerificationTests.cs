using Biolog.Features.AuthenticityAssessment.Model;
using Biolog.Features.DocumentClassifier.Model;
using Biolog.Features.DocumentTemplatePack.Audit;
using Biolog.Features.DocumentTemplatePack.Model;
using Biolog.Features.DocumentTemplatePack.Tests.Fixtures;
using Xunit;

namespace Biolog.Features.DocumentTemplatePack.Tests;

/// <summary>BIO-DOC-010-AC04: "pack version is recorded on every verification" -
/// <see cref="DocumentTemplateVerification.Verify"/> always stamps and durably audits the exact
/// governed pack/version/entry it ran against.</summary>
public sealed class DocumentTemplateVerificationTests
{
    private static (Model.DocumentTemplatePack Pack, InMemoryDocumentTemplatePackAuditSink AuditSink) PublishedPackWithOneEntry()
    {
        var pack = new Model.DocumentTemplatePack("verify-test-pack", "verify-test-pack-v1", "NGA");
        pack.AddEntry(TestEntries.SimpleEntry());
        var auditSink = new InMemoryDocumentTemplatePackAuditSink();
        pack.Publish("tester", "2026-01-01T00:00:00.000Z", "evt_publish-correlation", new DocumentIntelligenceRegistry(), auditSink);
        return (pack, auditSink);
    }

    [Fact]
    public void Verify_with_all_fields_valid_reaches_Consistent_with_zero_violations()
    {
        var (pack, auditSink) = PublishedPackWithOneEntry();

        var outcome = DocumentTemplateVerification.Verify(
            pack, "test-entry-1", TestEntries.ValidObservedFields(), "verifier-1", "evt_verify-correlation-1", "2026-01-01T00:01:00.000Z", auditSink);

        Assert.Equal(DocumentTemplateVerificationState.Consistent, outcome.State);
        Assert.Empty(outcome.Violations);
        Assert.Equal("verify-test-pack", outcome.PackId);
        Assert.Equal("verify-test-pack-v1", outcome.PackVersion);
        Assert.Equal("test-entry-1", outcome.EntryId);
    }

    [Fact]
    public void Verify_with_a_missing_required_field_reaches_Failed()
    {
        var (pack, auditSink) = PublishedPackWithOneEntry();

        var outcome = DocumentTemplateVerification.Verify(
            pack, "test-entry-1", TestEntries.MissingRequiredFieldObservedFields(), "verifier-1", "evt_verify-correlation-2", "2026-01-01T00:01:00.000Z", auditSink);

        Assert.Equal(DocumentTemplateVerificationState.Failed, outcome.State);
        Assert.Contains(outcome.Violations, v => v.FieldName == "Surname" && v.Reason.Contains("missing"));
    }

    [Fact]
    public void Verify_with_only_a_non_required_field_mismatch_reaches_Inconclusive_never_Failed()
    {
        var (pack, auditSink) = PublishedPackWithOneEntry();

        var outcome = DocumentTemplateVerification.Verify(
            pack, "test-entry-1", TestEntries.NonRequiredPatternMismatchObservedFields(), "verifier-1", "evt_verify-correlation-3", "2026-01-01T00:01:00.000Z", auditSink);

        Assert.Equal(DocumentTemplateVerificationState.Inconclusive, outcome.State);
        Assert.Single(outcome.Violations);
        Assert.Equal("OptionalNote", outcome.Violations[0].FieldName);
    }

    [Fact]
    public void Verify_always_durably_records_an_audit_event_carrying_the_exact_pack_version()
    {
        var (pack, auditSink) = PublishedPackWithOneEntry();
        var eventsBefore = auditSink.Events.Count;

        var outcome = DocumentTemplateVerification.Verify(
            pack, "test-entry-1", TestEntries.ValidObservedFields(), "verifier-1", "evt_verify-correlation-4", "2026-01-01T00:01:00.000Z", auditSink);

        Assert.Equal(eventsBefore + 1, auditSink.Events.Count);
        var recorded = auditSink.Events[^1];
        Assert.Equal("document_template_pack.verification_recorded", recorded.Action);
        Assert.Equal(pack.PackVersion, recorded.Detail["pack_version"]);
        Assert.Equal(outcome.RecordId, recorded.EventId);
    }

    [Fact]
    public void Verify_records_an_optional_correlated_authenticity_disposition_when_supplied()
    {
        var (pack, auditSink) = PublishedPackWithOneEntry();

        DocumentTemplateVerification.Verify(
            pack, "test-entry-1", TestEntries.ValidObservedFields(), "verifier-1", "evt_verify-correlation-5", "2026-01-01T00:01:00.000Z", auditSink,
            correlatedAuthenticityDisposition: AuthenticityDisposition.DocumentAuthentic);

        var recorded = auditSink.Events[^1];
        Assert.Equal(nameof(AuthenticityDisposition.DocumentAuthentic), recorded.Detail["correlated_authenticity_disposition"]);
    }

    [Fact]
    public void Verify_throws_PackNotPublishedException_against_a_draft_pack()
    {
        var pack = new Model.DocumentTemplatePack("draft-pack", "draft-pack-v1", "NGA");
        pack.AddEntry(TestEntries.SimpleEntry());
        var auditSink = new InMemoryDocumentTemplatePackAuditSink();

        Assert.Throws<PackNotPublishedException>(() => DocumentTemplateVerification.Verify(
            pack, "test-entry-1", TestEntries.ValidObservedFields(), "verifier-1", "evt_verify-correlation-6", "2026-01-01T00:01:00.000Z", auditSink));
    }

    [Fact]
    public void Verify_throws_EntryNotInPackException_for_an_unknown_entry_id()
    {
        var (pack, auditSink) = PublishedPackWithOneEntry();

        Assert.Throws<EntryNotInPackException>(() => DocumentTemplateVerification.Verify(
            pack, "no-such-entry", TestEntries.ValidObservedFields(), "verifier-1", "evt_verify-correlation-7", "2026-01-01T00:01:00.000Z", auditSink));
    }

    [Fact]
    public void Verify_rejects_a_blank_verifiedByActorId()
    {
        var (pack, auditSink) = PublishedPackWithOneEntry();

        Assert.Throws<ArgumentException>(() => DocumentTemplateVerification.Verify(
            pack, "test-entry-1", TestEntries.ValidObservedFields(), "  ", "evt_verify-correlation-8", "2026-01-01T00:01:00.000Z", auditSink));
    }
}
