using Biolog.Features.BarcodeEngine.Model;
using Biolog.Features.ParserRegistry.Model;
using Xunit;

namespace Biolog.Features.ParserRegistry.Tests;

/// <summary>Developer tests for BIO-DOC-005-AC05 ("tenant parser cannot inspect another
/// tenant").</summary>
public sealed class ParserIsolationTests
{
    private const string Now = "2026-09-22T12:00:00.000Z";

    private static ParserApplicability SampleApplicability() =>
        ParserApplicability.Create(BarcodeSymbology.Pdf417, "US-CA-DMV");

    private static IReadOnlyList<ParserFieldMapping> SampleFields() =>
        [ParserFieldMapping.Create("surname", 0, 10)];

    private static ParserCatalog BuildCatalogWithTenantAParser(out ParserRecord record)
    {
        var authority = new TenantParserAuthorityRegistry();
        authority.Grant("ten_a");
        var catalog = new ParserCatalog(authority);
        record = catalog.Draft(new ParserCaller.Tenant("ten_a"), "secret-parser", new ParserOwnerScope.Tenant("ten_a"),
            SampleApplicability(), SampleFields(), [], "author", Now);
        return catalog;
    }

    [Fact]
    public void AC05_tenant_a_can_read_its_own_parser()
    {
        var catalog = BuildCatalogWithTenantAParser(out _);

        var record = catalog.Get(new ParserCaller.Tenant("ten_a"), "secret-parser");

        Assert.Equal("secret-parser", record.ParserId);
    }

    [Fact]
    public void AC05_tenant_b_cannot_read_tenant_as_parser()
    {
        var catalog = BuildCatalogWithTenantAParser(out _);

        Assert.Throws<ParserNotFoundException>(() => catalog.Get(new ParserCaller.Tenant("ten_b"), "secret-parser"));
    }

    [Fact]
    public void AC05_a_nonexistent_parser_and_a_cross_tenant_parser_are_indistinguishable_to_the_caller()
    {
        var catalog = BuildCatalogWithTenantAParser(out _);

        var nonExistent = Record.Exception(() => catalog.Get(new ParserCaller.Tenant("ten_b"), "does-not-exist"));
        var crossTenant = Record.Exception(() => catalog.Get(new ParserCaller.Tenant("ten_b"), "secret-parser"));

        Assert.IsType<ParserNotFoundException>(nonExistent);
        Assert.IsType<ParserNotFoundException>(crossTenant);
        Assert.Equal(nonExistent.GetType(), crossTenant.GetType());
    }

    [Fact]
    public void AC05_bts_can_read_any_tenants_parser()
    {
        var catalog = BuildCatalogWithTenantAParser(out _);

        var record = catalog.Get(new ParserCaller.Bts(), "secret-parser");

        Assert.Equal("secret-parser", record.ParserId);
    }

    [Fact]
    public void AC05_tenant_b_cannot_revise_test_or_publish_tenant_as_parser()
    {
        var catalog = BuildCatalogWithTenantAParser(out var record);
        var tenantB = new ParserCaller.Tenant("ten_b");

        Assert.Throws<ParserNotFoundException>(() => catalog.ReviseDraft(
            tenantB, "secret-parser", SampleApplicability(), SampleFields(), [], "attacker", record.ConcurrencyVersion, Now));
        Assert.Throws<ParserNotFoundException>(() => catalog.RecordTestResult(
            tenantB, "secret-parser", true, "attacker", record.ConcurrencyVersion, Now));
        Assert.Throws<ParserNotFoundException>(() => catalog.Publish(
            tenantB, "secret-parser", "attacker", record.ConcurrencyVersion, Now));
    }

    [Fact]
    public void AC05_a_global_parser_is_visible_to_every_tenant()
    {
        var catalog = new ParserCatalog(new TenantParserAuthorityRegistry());
        catalog.Draft(new ParserCaller.Bts(), "global-1", new ParserOwnerScope.Global(),
            SampleApplicability(), SampleFields(), [], "author", Now);

        Assert.NotNull(catalog.Get(new ParserCaller.Tenant("ten_a"), "global-1"));
        Assert.NotNull(catalog.Get(new ParserCaller.Tenant("ten_b"), "global-1"));
    }

    [Fact]
    public void AC05_resolve_never_matches_another_tenants_published_parser()
    {
        var authority = new TenantParserAuthorityRegistry();
        authority.Grant("ten_a");
        var catalog = new ParserCatalog(authority);
        var record = catalog.Draft(new ParserCaller.Tenant("ten_a"), "p1", new ParserOwnerScope.Tenant("ten_a"),
            SampleApplicability(), SampleFields(), [], "author-1", Now);
        catalog.RecordTestResult(new ParserCaller.Tenant("ten_a"), "p1", true, "tester", record.ConcurrencyVersion, Now);
        catalog.Publish(new ParserCaller.Tenant("ten_a"), "p1", "approver", record.ConcurrencyVersion, Now);

        var resolvedForB = catalog.ResolvePublished(new ParserCaller.Tenant("ten_b"), BarcodeSymbology.Pdf417, "US-CA-DMV", null);

        Assert.Null(resolvedForB);
    }
}
