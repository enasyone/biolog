using Biolog.Features.BarcodeEngine.Model;
using Biolog.Features.DocumentClassifier.Model;
using Xunit;

namespace Biolog.Features.DocumentClassifier.Tests;

/// <summary>Developer tests for BIO-DOC-006-AC03 ("template selection uses versioned Document
/// Intelligence Registry entries").</summary>
public sealed class DocumentIntelligenceRegistryTests
{
    private static DocumentIntelligenceRegistryEntry Entry(
        string entryId = "uto-passport-v1",
        string country = "UTO",
        string documentClass = "passport",
        string series = "2013",
        DocumentSide side = DocumentSide.Front,
        int versionNumber = 1,
        string templateId = "uto-passport-datapage") =>
        new(entryId, country, documentClass, series, side, versionNumber, templateId, ExpectedSymbologies: []);

    [Fact]
    public void AC03_a_registered_entry_is_resolvable_by_its_full_identity()
    {
        var registry = new DocumentIntelligenceRegistry();
        var entry = Entry();
        registry.Register(entry);

        var resolved = registry.Match("UTO", "passport", "2013", DocumentSide.Front);

        Assert.Same(entry, resolved);
    }

    [Fact]
    public void AC03_an_unregistered_identity_resolves_to_null_not_a_guess()
    {
        var registry = new DocumentIntelligenceRegistry();
        registry.Register(Entry());

        var resolved = registry.Match("UTO", "passport", "2021", DocumentSide.Front);

        Assert.Null(resolved);
    }

    [Fact]
    public void AC03_entries_are_also_resolvable_by_the_template_they_declare()
    {
        var registry = new DocumentIntelligenceRegistry();
        var entry = Entry(templateId: "uto-passport-datapage");
        registry.Register(entry);

        Assert.Same(entry, registry.GetByTemplateId("uto-passport-datapage"));
        Assert.Null(registry.GetByTemplateId("not-registered"));
    }

    [Fact]
    public void AC03_registering_a_higher_version_for_the_same_identity_replaces_the_current_entry()
    {
        var registry = new DocumentIntelligenceRegistry();
        registry.Register(Entry(entryId: "v1", versionNumber: 1, templateId: "template-v1"));
        registry.Register(Entry(entryId: "v2", versionNumber: 2, templateId: "template-v2"));

        var resolved = registry.Match("UTO", "passport", "2013", DocumentSide.Front);

        Assert.NotNull(resolved);
        Assert.Equal("v2", resolved!.EntryId);
        Assert.Equal("template-v2", resolved.TemplateId);
    }

    [Fact]
    public void AC03_versioning_is_monotonic_a_stale_or_equal_version_is_rejected()
    {
        var registry = new DocumentIntelligenceRegistry();
        registry.Register(Entry(entryId: "v2", versionNumber: 2));

        Assert.Throws<StaleRegistryVersionException>(() => registry.Register(Entry(entryId: "v1-again", versionNumber: 1)));
        Assert.Throws<StaleRegistryVersionException>(() => registry.Register(Entry(entryId: "v2-again", versionNumber: 2)));
    }

    [Fact]
    public void AC03_different_sides_of_the_same_document_are_independent_registry_identities()
    {
        var registry = new DocumentIntelligenceRegistry();
        registry.Register(Entry(entryId: "front", side: DocumentSide.Front, templateId: "t-front"));
        registry.Register(Entry(entryId: "back", side: DocumentSide.Back, templateId: "t-back"));

        Assert.Equal("t-front", registry.Match("UTO", "passport", "2013", DocumentSide.Front)!.TemplateId);
        Assert.Equal("t-back", registry.Match("UTO", "passport", "2013", DocumentSide.Back)!.TemplateId);
    }

    [Fact]
    public void AC03_QA01_upgrading_a_version_retires_the_previous_versions_template_from_the_reverse_lookup()
    {
        var registry = new DocumentIntelligenceRegistry();
        registry.Register(Entry(entryId: "entry-v1", versionNumber: 1, templateId: "template-v1"));
        registry.Register(Entry(entryId: "entry-v2", versionNumber: 2, templateId: "template-v2"));

        var current = registry.Match("UTO", "passport", "2013", DocumentSide.Front);
        Assert.Equal("entry-v2", current!.EntryId);

        // The superseded v1 template must no longer resolve to anything - otherwise a
        // classification signal keyed by the retired template could still select the stale
        // (country, class, series, side) mapping it used to point at (BIO-DOC-006-QA-01).
        Assert.Null(registry.GetByTemplateId("template-v1"));
        Assert.Same(current, registry.GetByTemplateId("template-v2"));
    }

    [Fact]
    public void AC03_QA02_registering_a_different_identity_with_an_already_owned_template_evicts_the_former_owner()
    {
        var registry = new DocumentIntelligenceRegistry();
        registry.Register(Entry(entryId: "passport", documentClass: "passport", series: "2013", templateId: "shared-template"));
        registry.Register(Entry(entryId: "national-id", documentClass: "national-id", series: "2026", templateId: "shared-template"));

        // The template can only identify one CURRENT identity - registering it for national-id
        // must retire passport's own currency, never let both coexist while classification
        // (which only ever resolves through GetByTemplateId) silently follows the last write
        // (BIO-DOC-006-QA-02).
        Assert.Null(registry.Match("UTO", "passport", "2013", DocumentSide.Front));
        var current = registry.Match("UTO", "national-id", "2026", DocumentSide.Front);
        Assert.Equal("national-id", current!.EntryId);
        Assert.Same(current, registry.GetByTemplateId("shared-template"));
    }

    [Fact]
    public void AC03_QA02_registering_a_new_version_of_the_same_identity_with_the_same_template_id_is_not_an_eviction()
    {
        var registry = new DocumentIntelligenceRegistry();
        registry.Register(Entry(entryId: "v1", versionNumber: 1, templateId: "stable-template"));
        registry.Register(Entry(entryId: "v2", versionNumber: 2, templateId: "stable-template"));

        var current = registry.Match("UTO", "passport", "2013", DocumentSide.Front);
        Assert.Equal("v2", current!.EntryId);
        Assert.Same(current, registry.GetByTemplateId("stable-template"));
    }

    [Fact]
    public void AC03_an_entry_may_declare_expected_barcode_symbologies_as_corroborating_evidence()
    {
        var entry = Entry() with { ExpectedSymbologies = [BarcodeSymbology.Pdf417] };

        Assert.Contains(BarcodeSymbology.Pdf417, entry.ExpectedSymbologies);
    }
}
