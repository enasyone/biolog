using Biolog.Features.DocumentClassifier.Model;
using Biolog.Features.DocumentTemplatePack.Audit;
using Biolog.Features.DocumentTemplatePack.Model;
using Biolog.Features.DocumentTemplatePack.Store;
using Biolog.Features.DocumentTemplatePack.Tests.Fixtures;
using Xunit;

namespace Biolog.Features.DocumentTemplatePack.Tests;

/// <summary>BIO-DOC-010-AC03: "reference/test samples are governed and synthetic where
/// required".</summary>
public sealed class DocumentTemplateSampleGovernanceTests
{
    [Fact]
    public void a_sample_is_always_synthetic_there_is_no_way_to_construct_one_that_is_not()
    {
        // Structural guarantee, not a caller-supplied flag - see DocumentTemplateSample's own
        // remarks. Proven here by reflection: no constructor or factory parameter named anything
        // like "isSynthetic"/"synthetic" exists anywhere on this type.
        var parameterNames = typeof(DocumentTemplateSample).GetConstructors()
            .SelectMany(c => c.GetParameters())
            .Select(p => p.Name ?? string.Empty)
            .ToList();

        Assert.DoesNotContain(parameterNames, name => name.Contains("synthetic", StringComparison.OrdinalIgnoreCase));

        var sample = new DocumentTemplateSample("s1", "entry-1", DocumentSide.Front, "label", new Dictionary<string, string> { ["Field"] = "value" });
        Assert.True(sample.IsSynthetic);
    }

    [Fact]
    public void store_accepts_a_sample_whose_EntryId_the_pack_actually_governs()
    {
        var pack = new Model.DocumentTemplatePack("sample-pack", "sample-pack-v1", "NGA");
        pack.AddEntry(TestEntries.SimpleEntry());
        var store = new InMemoryDocumentTemplateSampleStore();

        var sample = new DocumentTemplateSample("s1", "test-entry-1", DocumentSide.Front, "happy-path", TestEntries.ValidObservedFields());
        store.Add(pack, sample);

        Assert.Single(store.GetForEntry("test-entry-1"));
    }

    [Fact]
    public void store_rejects_a_sample_naming_an_entry_the_pack_does_not_govern()
    {
        var pack = new Model.DocumentTemplatePack("sample-pack-2", "sample-pack-2-v1", "NGA");
        pack.AddEntry(TestEntries.SimpleEntry());
        var store = new InMemoryDocumentTemplateSampleStore();

        var sample = new DocumentTemplateSample("s2", "no-such-entry", DocumentSide.Front, "orphan", TestEntries.ValidObservedFields());

        Assert.Throws<UngovernedSampleEntryException>(() => store.Add(pack, sample));
        Assert.Empty(store.GetForEntry("no-such-entry"));
    }

    [Fact]
    public void a_registered_samples_field_values_actually_satisfy_its_own_entrys_validation_rules()
    {
        // A "happy-path" governed sample that would itself fail the entry's own rules would be a
        // contradiction in the governance this story exists to provide.
        var pack = new Model.DocumentTemplatePack("sample-pack-3", "sample-pack-3-v1", "NGA");
        var entry = TestEntries.SimpleEntry();
        pack.AddEntry(entry);
        var auditSink = new InMemoryDocumentTemplatePackAuditSink();
        pack.Publish("tester", "2026-01-01T00:00:00.000Z", "evt_publish-correlation", new DocumentIntelligenceRegistry(), auditSink);

        var sampleStore = new InMemoryDocumentTemplateSampleStore();
        var sample = new DocumentTemplateSample("s3", entry.EntryId, DocumentSide.Front, "happy-path", TestEntries.ValidObservedFields());
        sampleStore.Add(pack, sample);

        var outcome = DocumentTemplateVerification.Verify(
            pack, entry.EntryId, sample.FieldValues, "verifier-1", "evt_verify-correlation", "2026-01-01T00:01:00.000Z", auditSink);

        Assert.Equal(DocumentTemplateVerificationState.Consistent, outcome.State);
    }
}
