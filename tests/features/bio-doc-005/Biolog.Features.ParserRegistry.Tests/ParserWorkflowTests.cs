using Biolog.Features.BarcodeEngine.Model;
using Biolog.Features.ParserRegistry.Model;
using Xunit;

namespace Biolog.Features.ParserRegistry.Tests;

/// <summary>Developer tests for BIO-DOC-005-AC02 ("versioned, tested and published through
/// approval") and AC04 ("published parser changes create a new version").</summary>
public sealed class ParserWorkflowTests
{
    private const string Now = "2026-09-22T12:00:00.000Z";
    private static readonly ParserCaller.Bts Bts = new();

    private static ParserCatalog NewCatalog() => new(new TenantParserAuthorityRegistry());

    private static ParserApplicability SampleApplicability() =>
        ParserApplicability.Create(BarcodeSymbology.Pdf417, "US-CA-DMV");

    private static IReadOnlyList<ParserFieldMapping> SampleFields() =>
        [ParserFieldMapping.Create("surname", 0, 10)];

    [Fact]
    public void AC02_a_fresh_draft_starts_as_version_1_in_draft_status()
    {
        var catalog = NewCatalog();
        var record = catalog.Draft(Bts, "p1", new ParserOwnerScope.Global(), SampleApplicability(), SampleFields(), [], "author", Now);

        Assert.Equal(1, record.Latest.VersionNumber);
        Assert.Equal(ParserStatus.Draft, record.Latest.Status);
        Assert.Null(record.PublishedVersionNumber);
    }

    [Fact]
    public void AC02_a_passing_test_promotes_draft_to_tested()
    {
        var catalog = NewCatalog();
        var record = catalog.Draft(Bts, "p1", new ParserOwnerScope.Global(), SampleApplicability(), SampleFields(), [], "author", Now);

        catalog.RecordTestResult(Bts, "p1", passed: true, "tester", record.ConcurrencyVersion, Now);

        Assert.Equal(ParserStatus.Tested, record.Latest.Status);
    }

    [Fact]
    public void AC02_a_failing_test_leaves_the_version_in_draft_unrevised()
    {
        var catalog = NewCatalog();
        var record = catalog.Draft(Bts, "p1", new ParserOwnerScope.Global(), SampleApplicability(), SampleFields(), [], "author", Now);

        catalog.RecordTestResult(Bts, "p1", passed: false, "tester", record.ConcurrencyVersion, Now);

        Assert.Equal(ParserStatus.Draft, record.Latest.Status);
    }

    [Fact]
    public void AC02_publish_requires_the_version_to_have_passed_testing_first()
    {
        var catalog = NewCatalog();
        var record = catalog.Draft(Bts, "p1", new ParserOwnerScope.Global(), SampleApplicability(), SampleFields(), [], "author", Now);

        Assert.Throws<InvalidOperationException>(() =>
            catalog.Publish(Bts, "p1", "approver", record.ConcurrencyVersion, Now));
    }

    [Fact]
    public void AC02_publish_requires_a_different_approver_from_the_author_maker_checker()
    {
        var catalog = NewCatalog();
        var record = catalog.Draft(Bts, "p1", new ParserOwnerScope.Global(), SampleApplicability(), SampleFields(), [], "author-1", Now);
        catalog.RecordTestResult(Bts, "p1", passed: true, "tester", record.ConcurrencyVersion, Now);

        Assert.Throws<InvalidOperationException>(() =>
            catalog.Publish(Bts, "p1", "author-1", record.ConcurrencyVersion, Now));
    }

    [Fact]
    public void AC02_a_valid_publish_marks_the_version_published_and_records_the_approver()
    {
        var catalog = NewCatalog();
        var record = catalog.Draft(Bts, "p1", new ParserOwnerScope.Global(), SampleApplicability(), SampleFields(), [], "author-1", Now);
        catalog.RecordTestResult(Bts, "p1", passed: true, "tester", record.ConcurrencyVersion, Now);

        catalog.Publish(Bts, "p1", "approver-1", record.ConcurrencyVersion, Now);

        Assert.Equal(ParserStatus.Published, record.Latest.Status);
        Assert.Equal("approver-1", record.Latest.PublishedByActorId);
        Assert.Equal(1, record.PublishedVersionNumber);
    }

    [Fact]
    public void AC04_revising_an_unpublished_draft_edits_it_in_place_no_new_version()
    {
        var catalog = NewCatalog();
        var record = catalog.Draft(Bts, "p1", new ParserOwnerScope.Global(), SampleApplicability(), SampleFields(), [], "author", Now);

        catalog.ReviseDraft(Bts, "p1", SampleApplicability(), [ParserFieldMapping.Create("given_names", 10, 10)], [], "author", record.ConcurrencyVersion, Now);

        Assert.Single(record.Versions);
        Assert.Equal(1, record.Latest.VersionNumber);
    }

    [Fact]
    public void AC04_revising_a_published_parser_creates_a_new_version_never_mutates_the_published_one()
    {
        var catalog = NewCatalog();
        var record = catalog.Draft(Bts, "p1", new ParserOwnerScope.Global(), SampleApplicability(), SampleFields(), [], "author-1", Now);
        catalog.RecordTestResult(Bts, "p1", passed: true, "tester", record.ConcurrencyVersion, Now);
        catalog.Publish(Bts, "p1", "approver-1", record.ConcurrencyVersion, Now);

        catalog.ReviseDraft(Bts, "p1", SampleApplicability(), [ParserFieldMapping.Create("given_names", 10, 10)], [], "author-1", record.ConcurrencyVersion, Now);

        Assert.Equal(2, record.Versions.Count);
        Assert.Equal(2, record.Latest.VersionNumber);
        Assert.Equal(ParserStatus.Draft, record.Latest.Status);
        // The originally-published version 1 is untouched.
        var published = record.Versions.Single(v => v.VersionNumber == 1);
        Assert.Equal(ParserStatus.Published, published.Status);
        Assert.Equal("surname", published.FieldMappings[0].FieldName);
        Assert.Equal(1, record.PublishedVersionNumber);
    }

    [Fact]
    public void AC02_a_stale_concurrency_version_is_rejected_adr_0001()
    {
        var catalog = NewCatalog();
        var record = catalog.Draft(Bts, "p1", new ParserOwnerScope.Global(), SampleApplicability(), SampleFields(), [], "author", Now);

        Assert.Throws<ConcurrencyException>(() =>
            catalog.RecordTestResult(Bts, "p1", passed: true, "tester", record.ConcurrencyVersion + 1, Now));
    }
}
