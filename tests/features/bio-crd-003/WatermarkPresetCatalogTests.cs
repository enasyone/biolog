using Biolog.Features.CardTypography.Domain;
using Xunit;

namespace Biolog.Features.CardTypography.Tests;

public sealed class WatermarkPresetCatalogTests
{
    [Theory]
    [InlineData(WatermarkPresetKind.Staff, "STAFF")]
    [InlineData(WatermarkPresetKind.Student, "STUDENT")]
    [InlineData(WatermarkPresetKind.Visitor, "VISITOR")]
    public void AC05_every_preset_kind_has_a_starter_behaviour(WatermarkPresetKind kind, string expectedText)
    {
        var preset = WatermarkPresetCatalog.TryGetPreset(kind);
        Assert.NotNull(preset);
        Assert.Equal(expectedText, preset!.Text);
        Assert.InRange(preset.DefaultOpacityPercent, 0, 100);
    }

    [Fact]
    public void AC05_the_catalogue_has_exactly_three_presets()
    {
        Assert.Equal(3, WatermarkPresetCatalog.Presets.Count);
    }
}
