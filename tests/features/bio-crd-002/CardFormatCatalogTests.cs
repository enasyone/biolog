using Biolog.Features.CardDesigner.Domain;
using Xunit;

namespace Biolog.Features.CardDesigner.Tests;

public sealed class CardFormatCatalogTests
{
    [Fact]
    public void AC01_cr80_matches_the_iso_iec_7810_id_1_dimensions()
    {
        var size = CardFormatCatalog.TryGetStandardSize(CardFormatKind.Cr80);
        Assert.NotNull(size);
        Assert.Equal(85.60, size!.LongEdgeMm);
        Assert.Equal(53.98, size.ShortEdgeMm);
    }

    [Fact]
    public void AC01_cr79_matches_the_published_industry_dimensions()
    {
        var size = CardFormatCatalog.TryGetStandardSize(CardFormatKind.Cr79);
        Assert.NotNull(size);
        Assert.Equal(83.90, size!.LongEdgeMm);
        Assert.Equal(51.90, size.ShortEdgeMm);
    }

    [Fact]
    public void AC01_cr100_matches_the_published_industry_dimensions()
    {
        var size = CardFormatCatalog.TryGetStandardSize(CardFormatKind.Cr100);
        Assert.NotNull(size);
        Assert.Equal(98.50, size!.LongEdgeMm);
        Assert.Equal(67.00, size.ShortEdgeMm);
    }

    [Fact]
    public void AC01_custom_has_no_standard_size()
    {
        Assert.Null(CardFormatCatalog.TryGetStandardSize(CardFormatKind.Custom));
    }

    [Fact]
    public void AC01_every_standard_formats_long_edge_is_strictly_greater_than_its_short_edge()
    {
        // Sanity invariant: a "long edge"/"short edge" pair that isn't actually long/short would
        // silently break every orientation computation built on top of it.
        foreach (var (kind, size) in CardFormatCatalog.Standard)
        {
            Assert.True(size.LongEdgeMm > size.ShortEdgeMm, $"{kind}: long edge must exceed short edge");
        }
    }
}
