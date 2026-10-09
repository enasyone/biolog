using Biolog.Features.BarcodeEngine.Model;
using Biolog.Features.ParserRegistry.Model;
using Xunit;

namespace Biolog.Features.ParserRegistry.Tests;

/// <summary>Developer tests for BIO-DOC-005-AC01 ("BTS and permitted tenants can define
/// parsers").</summary>
public sealed class ParserCatalogAuthorshipTests
{
    private const string Now = "2026-09-22T12:00:00.000Z";

    private static ParserApplicability SampleApplicability() =>
        ParserApplicability.Create(BarcodeSymbology.Pdf417, "US-CA-DMV");

    private static IReadOnlyList<ParserFieldMapping> SampleFields() =>
        [ParserFieldMapping.Create("surname", 0, 10)];

    [Fact]
    public void AC01_bts_can_author_a_global_parser()
    {
        var catalog = new ParserCatalog(new TenantParserAuthorityRegistry());

        var record = catalog.Draft(
            new ParserCaller.Bts(), "global-1", new ParserOwnerScope.Global(),
            SampleApplicability(), SampleFields(), [], "author-1", Now);

        Assert.Equal("global-1", record.ParserId);
    }

    [Fact]
    public void AC01_a_tenant_cannot_author_a_global_parser()
    {
        var catalog = new ParserCatalog(new TenantParserAuthorityRegistry());

        Assert.Throws<InvalidOperationException>(() => catalog.Draft(
            new ParserCaller.Tenant("ten_a"), "global-1", new ParserOwnerScope.Global(),
            SampleApplicability(), SampleFields(), [], "author-1", Now));
    }

    [Fact]
    public void AC01_an_authorized_tenant_can_author_its_own_parser()
    {
        var authority = new TenantParserAuthorityRegistry();
        authority.Grant("ten_a");
        var catalog = new ParserCatalog(authority);

        var record = catalog.Draft(
            new ParserCaller.Tenant("ten_a"), "tenant-a-1", new ParserOwnerScope.Tenant("ten_a"),
            SampleApplicability(), SampleFields(), [], "author-1", Now);

        Assert.Equal("tenant-a-1", record.ParserId);
    }

    [Fact]
    public void AC01_an_unauthorized_tenant_cannot_author_a_parser()
    {
        var catalog = new ParserCatalog(new TenantParserAuthorityRegistry()); // ten_a never granted

        Assert.Throws<InvalidOperationException>(() => catalog.Draft(
            new ParserCaller.Tenant("ten_a"), "tenant-a-1", new ParserOwnerScope.Tenant("ten_a"),
            SampleApplicability(), SampleFields(), [], "author-1", Now));
    }

    [Fact]
    public void AC01_a_tenant_cannot_author_a_parser_scoped_to_a_different_tenant()
    {
        var authority = new TenantParserAuthorityRegistry();
        authority.Grant("ten_a");
        var catalog = new ParserCatalog(authority);

        Assert.Throws<InvalidOperationException>(() => catalog.Draft(
            new ParserCaller.Tenant("ten_a"), "for-b", new ParserOwnerScope.Tenant("ten_b"),
            SampleApplicability(), SampleFields(), [], "author-1", Now));
    }

    [Fact]
    public void AC01_bts_can_author_on_behalf_of_any_tenant_without_a_grant()
    {
        var catalog = new ParserCatalog(new TenantParserAuthorityRegistry()); // no grants at all

        var record = catalog.Draft(
            new ParserCaller.Bts(), "for-a", new ParserOwnerScope.Tenant("ten_a"),
            SampleApplicability(), SampleFields(), [], "author-1", Now);

        Assert.Equal("for-a", record.ParserId);
    }

    [Fact]
    public void a_second_parser_with_the_same_id_is_rejected()
    {
        var authority = new TenantParserAuthorityRegistry();
        authority.Grant("ten_a");
        var catalog = new ParserCatalog(authority);
        catalog.Draft(new ParserCaller.Tenant("ten_a"), "dup", new ParserOwnerScope.Tenant("ten_a"),
            SampleApplicability(), SampleFields(), [], "author-1", Now);

        Assert.Throws<InvalidOperationException>(() => catalog.Draft(
            new ParserCaller.Tenant("ten_a"), "dup", new ParserOwnerScope.Tenant("ten_a"),
            SampleApplicability(), SampleFields(), [], "author-1", Now));
    }
}
