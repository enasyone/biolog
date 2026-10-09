using Biolog.Features.DocumentClassifier.Model;
using Biolog.Features.DocumentTemplatePack.Audit;
using Biolog.Features.DocumentTemplatePack.Data;
using Biolog.Features.DocumentTemplatePack.Model;
using Xunit;

namespace Biolog.Features.DocumentTemplatePack.Tests;

/// <summary>Sanity checks over the actual governed Nigeria Document Intelligence Pack v1 seed
/// content (BIO-DOC-010's own user story), independent of the generic fixtures
/// <see cref="DocumentTemplatePackEntryTests"/>/<see cref="DocumentTemplateVerificationTests"/>
/// use.</summary>
public sealed class NigeriaDocumentPackV1Tests
{
    [Fact]
    public void Build_returns_a_draft_pack_with_all_three_named_entries()
    {
        var pack = NigeriaDocumentPackV1.Build();

        Assert.Equal(DocumentTemplatePackStatus.Draft, pack.Status);
        Assert.Equal(3, pack.Entries.Count);
        Assert.NotNull(pack.FindEntry(NigeriaDocumentPackV1.PassportEntryId));
        Assert.NotNull(pack.FindEntry(NigeriaDocumentPackV1.NinSlipEntryId));
        Assert.NotNull(pack.FindEntry(NigeriaDocumentPackV1.DriversLicenceEntryId));
    }

    [Fact]
    public void every_entry_declares_at_least_one_required_field_so_Failed_is_reachable()
    {
        foreach (var entry in NigeriaDocumentPackV1.Build().Entries)
        {
            Assert.Contains(entry.ValidationRules, r => r.Required);
        }
    }

    [Fact]
    public void every_governed_samples_field_values_satisfy_its_own_entrys_rules()
    {
        var pack = NigeriaDocumentPackV1.Build();
        var auditSink = new InMemoryDocumentTemplatePackAuditSink();
        pack.Publish("tester", "2026-01-01T00:00:00.000Z", "evt_publish-correlation", new DocumentIntelligenceRegistry(), auditSink);

        foreach (var sample in NigeriaDocumentPackV1.Samples())
        {
            var outcome = DocumentTemplateVerification.Verify(
                pack, sample.EntryId, sample.FieldValues, "verifier-1", $"evt_verify-{sample.SampleId}", "2026-01-01T00:01:00.000Z", auditSink);
            Assert.Equal(DocumentTemplateVerificationState.Consistent, outcome.State);
        }
    }

    [Fact]
    public void DemoScenarios_exercises_every_verification_state_exactly_once_across_three_calls()
    {
        var pack = NigeriaDocumentPackV1.Build();
        var auditSink = new InMemoryDocumentTemplatePackAuditSink();
        pack.Publish("tester", "2026-01-01T00:00:00.000Z", "evt_publish-correlation", new DocumentIntelligenceRegistry(), auditSink);

        var states = NigeriaDocumentPackV1.DemoScenarios()
            .Select(s => DocumentTemplateVerification.Verify(pack, s.EntryId, s.ObservedFields, "verifier-1", $"evt_verify-{s.EntryId}", "2026-01-01T00:01:00.000Z", auditSink).State)
            .ToList();

        Assert.Contains(DocumentTemplateVerificationState.Consistent, states);
        Assert.Contains(DocumentTemplateVerificationState.Failed, states);
        Assert.Contains(DocumentTemplateVerificationState.Inconclusive, states);
    }
}
