using Biolog.Features.CardDesigner.Domain;
using Xunit;

namespace Biolog.Features.CardDesigner.Tests;

public sealed class CustomCardSizeValidatorTests
{
    [Fact]
    public void AC03_a_size_within_bounds_is_valid()
    {
        var result = CustomCardSizeValidator.Validate(85.60, 53.98);
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void AC03_the_bounds_themselves_are_valid_inclusive()
    {
        var result = CustomCardSizeValidator.Validate(CustomCardSizeValidator.MinEdgeMm, CustomCardSizeValidator.MaxEdgeMm);
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(19.99, 50.0)]
    [InlineData(50.0, 19.99)]
    public void AC03_a_size_just_below_the_minimum_is_rejected(double width, double height)
    {
        var result = CustomCardSizeValidator.Validate(width, height);
        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
    }

    [Theory]
    [InlineData(200.01, 50.0)]
    [InlineData(50.0, 200.01)]
    public void AC03_a_size_just_above_the_maximum_is_rejected(double width, double height)
    {
        var result = CustomCardSizeValidator.Validate(width, height);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void AC03_zero_or_negative_dimensions_are_rejected()
    {
        var result = CustomCardSizeValidator.Validate(0, -10);
        Assert.False(result.IsValid);
        Assert.Equal(2, result.Errors.Count);
    }

    [Fact]
    public void AC03_nan_and_infinity_are_rejected_not_silently_accepted()
    {
        var result = CustomCardSizeValidator.Validate(double.NaN, double.PositiveInfinity);
        Assert.False(result.IsValid);
        Assert.Equal(2, result.Errors.Count);
    }

    [Fact]
    public void AC03_both_dimensions_out_of_bounds_report_both_errors_together()
    {
        var result = CustomCardSizeValidator.Validate(1, 1000);
        Assert.False(result.IsValid);
        Assert.Equal(2, result.Errors.Count);
    }
}
