using Biolog.Features.DocumentClassifier.Model;
using Biolog.Features.DocumentTemplatePack.Audit;
using Biolog.Features.DocumentTemplatePack.Model;
using Biolog.Features.DocumentTemplatePack.Tests.Fixtures;
using Xunit;

namespace Biolog.Features.DocumentTemplatePack.Tests;

/// <summary>BIO-DOC-010-AC02 ("templates are immutable once published") and the BIO-DOC-006
/// integration <see cref="Model.DocumentTemplatePack.Publish"/> performs.</summary>
public sealed class DocumentTemplatePackTests
{
    private static Model.DocumentTemplatePack NewDraftPack() => new("test-pack", "test-pack-v1", "NGA");

    [Fact]
    public void AddEntry_then_Publish_transitions_Draft_to_Published()
    {
        var pack = NewDraftPack();
        pack.AddEntry(TestEntries.SimpleEntry());
        Assert.Equal(DocumentTemplatePackStatus.Draft, pack.Status);

        pack.Publish("tester", "2026-01-01T00:00:00.000Z", "evt_test-correlation", new DocumentIntelligenceRegistry(), new InMemoryDocumentTemplatePackAuditSink());

        Assert.Equal(DocumentTemplatePackStatus.Published, pack.Status);
        Assert.Equal("tester", pack.PublishedBy);
    }

    [Fact]
    public void Publish_registers_every_sides_identity_into_the_supplied_BIO_DOC_006_registry()
    {
        var pack = NewDraftPack();
        pack.AddEntry(TestEntries.SimpleEntry("entry-a"));
        var registry = new DocumentIntelligenceRegistry();

        pack.Publish("tester", "2026-01-01T00:00:00.000Z", "evt_test-correlation", registry, new InMemoryDocumentTemplatePackAuditSink());

        var registered = registry.GetByTemplateId("tpl-entry-a-front");
        Assert.NotNull(registered);
        Assert.Equal("NGA", registered!.Country);
        Assert.Equal("test-document", registered.DocumentClass);
        Assert.Equal("test-series", registered.Series);
        Assert.Equal(DocumentSide.Front, registered.Side);
        Assert.Equal(1, registered.VersionNumber);

        var matched = registry.Match("NGA", "test-document", "test-series", DocumentSide.Front);
        Assert.NotNull(matched);
        Assert.Equal("tpl-entry-a-front", matched!.TemplateId);
    }

    [Fact]
    public void Publish_writes_a_durable_audit_event_before_returning()
    {
        var pack = NewDraftPack();
        pack.AddEntry(TestEntries.SimpleEntry());
        var auditSink = new InMemoryDocumentTemplatePackAuditSink();

        pack.Publish("tester", "2026-01-01T00:00:00.000Z", "evt_test-correlation", new DocumentIntelligenceRegistry(), auditSink);

        var published = Assert.Single(auditSink.Events);
        Assert.Equal("document_template_pack.published", published.Action);
        Assert.Equal("test-pack", published.Target.Id);
    }

    [Fact]
    public void AddEntry_throws_once_the_pack_is_published()
    {
        var pack = NewDraftPack();
        pack.AddEntry(TestEntries.SimpleEntry());
        pack.Publish("tester", "2026-01-01T00:00:00.000Z", "evt_test-correlation", new DocumentIntelligenceRegistry(), new InMemoryDocumentTemplatePackAuditSink());

        Assert.Throws<DocumentTemplatePackAlreadyPublishedException>(() => pack.AddEntry(TestEntries.SimpleEntry("another-entry")));
    }

    [Fact]
    public void Publish_throws_a_second_time_on_the_same_pack_instance()
    {
        var pack = NewDraftPack();
        pack.AddEntry(TestEntries.SimpleEntry());
        var registry = new DocumentIntelligenceRegistry();
        var auditSink = new InMemoryDocumentTemplatePackAuditSink();
        pack.Publish("tester", "2026-01-01T00:00:00.000Z", "evt_test-correlation", registry, auditSink);

        Assert.Throws<DocumentTemplatePackAlreadyPublishedException>(
            () => pack.Publish("tester", "2026-01-01T00:00:01.000Z", "evt_test-correlation-2", registry, auditSink));
    }

    [Fact]
    public void Publish_throws_when_the_pack_has_no_entries()
    {
        var pack = NewDraftPack();
        Assert.Throws<EmptyPackCannotBePublishedException>(
            () => pack.Publish("tester", "2026-01-01T00:00:00.000Z", "evt_test-correlation", new DocumentIntelligenceRegistry(), new InMemoryDocumentTemplatePackAuditSink()));
    }

    [Fact]
    public void AddEntry_throws_on_a_duplicate_EntryId_even_before_publish()
    {
        var pack = NewDraftPack();
        pack.AddEntry(TestEntries.SimpleEntry("dup"));
        Assert.Throws<DuplicatePackEntryException>(() => pack.AddEntry(TestEntries.SimpleEntry("dup")));
    }

    [Fact]
    public void there_is_no_way_to_remove_or_replace_an_entry_once_added()
    {
        // Structural proof of AC02's "immutable once published", extended to the whole
        // lifecycle: DocumentTemplatePack exposes no Remove/Replace method at all, at any
        // Draft/Published stage - an entry is added once, never edited in place.
        var methodNames = typeof(Model.DocumentTemplatePack).GetMethods()
            .Select(m => m.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain("RemoveEntry", methodNames);
        Assert.DoesNotContain("ReplaceEntry", methodNames);
        Assert.DoesNotContain("UpdateEntry", methodNames);
    }

    [Fact]
    public void Publish_rejects_a_bare_date_for_publishedAt()
    {
        var pack = NewDraftPack();
        pack.AddEntry(TestEntries.SimpleEntry());
        var ex = Assert.Throws<ArgumentException>(() => pack.Publish(
            "tester", "2026-01-01", "evt_test-correlation", new DocumentIntelligenceRegistry(), new InMemoryDocumentTemplatePackAuditSink()));
        Assert.Contains("RFC 3339", ex.Message);
    }
}
