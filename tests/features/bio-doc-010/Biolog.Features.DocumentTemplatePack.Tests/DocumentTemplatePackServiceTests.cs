using Biolog.Features.DocumentTemplatePack.Api;
using Biolog.Features.DocumentTemplatePack.Audit;
using Biolog.Features.DocumentTemplatePack.Data;
using Biolog.Features.DocumentTemplatePack.Model;
using Biolog.Features.DocumentTemplatePack.Store;
using Xunit;

namespace Biolog.Features.DocumentTemplatePack.Tests;

/// <summary>Exercises <see cref="DocumentTemplatePackService"/> - the application layer
/// <c>DocumentTemplatePackEndpoints.IdentifyAsync</c> calls (BIO-DOC-010-AC05).</summary>
public sealed class DocumentTemplatePackServiceTests
{
    private static DocumentTemplatePackService NewService() =>
        new(new InMemoryDocumentTemplatePackStore(), new InMemoryDocumentTemplateSampleStore(), new InMemoryDocumentTemplatePackAuditSink());

    [Fact]
    public void construction_publishes_the_Nigeria_pack_and_registers_all_three_entries_sides()
    {
        var service = NewService();

        Assert.Equal(DocumentTemplatePackStatus.Published, service.CurrentPack.Status);
        Assert.Equal(NigeriaDocumentPackV1.PackVersion, service.CurrentPack.PackVersion);
        Assert.Equal(3, service.CurrentPack.Entries.Count);

        // The driver's licence entry declares two sides (AC01) - both must have been registered.
        Assert.NotNull(service.Registry.GetByTemplateId("tpl-nga-dl-2018-front"));
        Assert.NotNull(service.Registry.GetByTemplateId("tpl-nga-dl-2018-back"));
    }

    [Fact]
    public void construction_registers_a_governed_synthetic_sample_for_every_entry()
    {
        var service = NewService();

        foreach (var entry in service.CurrentPack.Entries)
        {
            Assert.NotEmpty(service.SamplesFor(entry.EntryId));
        }
    }

    [Fact]
    public void IdentifyNextDemo_cycles_deterministically_through_all_three_verification_states()
    {
        var service = NewService();

        var first = service.IdentifyNextDemo("actor-1", "evt_correlation-1");
        var second = service.IdentifyNextDemo("actor-1", "evt_correlation-2");
        var third = service.IdentifyNextDemo("actor-1", "evt_correlation-3");
        var fourth = service.IdentifyNextDemo("actor-1", "evt_correlation-4");

        Assert.Equal(DocumentTemplateVerificationState.Consistent, first.State);
        Assert.Equal(DocumentTemplateVerificationState.Failed, second.State);
        Assert.Equal(DocumentTemplateVerificationState.Inconclusive, third.State);
        Assert.Equal(first.EntryId, fourth.EntryId); // cycles back to the first entry
    }

    [Fact]
    public void two_independent_service_instances_never_share_state()
    {
        // The exact desynchronisation shape BIO-DOC-009-F01's own follow-on bug had
        // (SyntheticAuthenticityFixtures's static store) - proactively guarded against here by
        // construction (every field is instance-owned), proven directly: two fresh instances
        // both start their own demo cycle at the same first scenario.
        var serviceA = NewService();
        var serviceB = NewService();

        var fromA = serviceA.IdentifyNextDemo("actor-a", "evt_a-1");
        var fromB = serviceB.IdentifyNextDemo("actor-b", "evt_b-1");

        Assert.Equal(fromA.EntryId, fromB.EntryId);
        Assert.Equal(fromA.State, fromB.State);
    }
}
