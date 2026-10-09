// Developer tests for BIO-CRD-004. SYNTHETIC fixtures only.
using Biolog.Features.CardObjects.Domain;
using Xunit;

namespace Biolog.Features.CardObjects.Tests;

public sealed class BarcodeValidationTests
{
    private static BarcodeValidationRequest Valid() => new(
        BarcodeProfile.Alphanumeric, QuietZoneMm: 3.0, WidthMm: 20.0, HeightMm: 10.0,
        ForegroundColorHex: "#000000", BackgroundColorHex: "#ffffff");

    [Fact]
    public void AC03_a_well_formed_barcode_configuration_is_valid()
    {
        var result = BarcodeValidation.Validate(Valid());
        Assert.True(result.IsValid);
        Assert.Empty(result.Violations);
    }

    [Fact]
    public void AC03_a_quiet_zone_below_the_minimum_is_rejected()
    {
        var result = BarcodeValidation.Validate(Valid() with { QuietZoneMm = BarcodeValidation.MinQuietZoneMm - 0.1 });
        Assert.False(result.IsValid);
        Assert.Contains(result.Violations, v => v.Contains("quiet_zone_mm"));
    }

    [Fact]
    public void AC03_a_quiet_zone_exactly_at_the_minimum_is_accepted()
    {
        var result = BarcodeValidation.Validate(Valid() with { QuietZoneMm = BarcodeValidation.MinQuietZoneMm });
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(1.0, 10.0)]
    [InlineData(200.0, 10.0)]
    [InlineData(10.0, 1.0)]
    [InlineData(10.0, 200.0)]
    public void AC03_a_size_outside_the_dimension_bounds_is_rejected(double widthMm, double heightMm)
    {
        var result = BarcodeValidation.Validate(Valid() with { WidthMm = widthMm, HeightMm = heightMm });
        Assert.False(result.IsValid);
    }

    [Fact]
    public void AC03_identical_foreground_and_background_colours_fail_the_contrast_check()
    {
        var result = BarcodeValidation.Validate(Valid() with { ForegroundColorHex = "#808080", BackgroundColorHex = "#808080" });
        Assert.False(result.IsValid);
        Assert.Contains(result.Violations, v => v.Contains("contrast_ratio"));
    }

    [Fact]
    public void AC03_black_on_white_has_the_maximum_possible_contrast_ratio()
    {
        var ratio = BarcodeValidation.ContrastRatio((0, 0, 0), (255, 255, 255));
        Assert.Equal(21.0, ratio, precision: 1);
    }

    [Fact]
    public void AC03_a_malformed_colour_is_rejected_without_throwing()
    {
        var result = BarcodeValidation.Validate(Valid() with { ForegroundColorHex = "not-a-colour" });
        Assert.False(result.IsValid);
        Assert.Contains(result.Violations, v => v.Contains("foreground_color_hex"));
    }

    [Fact]
    public void AC03_multiple_violations_are_all_reported_together()
    {
        var result = BarcodeValidation.Validate(Valid() with { QuietZoneMm = 0, WidthMm = 0 });
        Assert.False(result.IsValid);
        Assert.True(result.Violations.Count >= 2);
    }
}
