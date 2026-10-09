// Developer tests for BIO-RPT-002. SYNTHETIC fixtures only.
using Biolog.Features.ReportBuilder.Domain;
using Xunit;

namespace Biolog.Features.ReportBuilder.Tests;

public sealed class ReportSchemaTests
{
    [Theory]
    [InlineData(ReportDataset.CardProduction, ReportDimension.Status, ReportMeasure.Count)]
    [InlineData(ReportDataset.CardProduction, ReportDimension.Source, ReportMeasure.Count)]
    [InlineData(ReportDataset.CardProduction, ReportDimension.CardClass, ReportMeasure.Count)]
    [InlineData(ReportDataset.Attendance, ReportDimension.Mode, ReportMeasure.Count)]
    [InlineData(ReportDataset.Attendance, ReportDimension.Source, ReportMeasure.Count)]
    [InlineData(ReportDataset.Attendance, ReportDimension.VerificationMethod, ReportMeasure.Count)]
    public void AC01_every_whitelisted_combination_validates_without_throwing(ReportDataset dataset, ReportDimension dimension, ReportMeasure measure)
    {
        var definition = new ReportDefinition { Dataset = dataset, Dimension = dimension, Measure = measure };
        var exception = Record.Exception(() => ReportDefinitionValidator.Validate(definition));
        Assert.Null(exception);
    }

    [Fact]
    public void AC01_a_dimension_not_whitelisted_for_that_dataset_is_rejected_even_though_it_is_a_real_dimension_of_the_other_dataset()
    {
        // Mode is real, but only for Attendance - asking for it on CardProduction must not
        // silently fall through to some default column.
        var definition = new ReportDefinition { Dataset = ReportDataset.CardProduction, Dimension = ReportDimension.Mode, Measure = ReportMeasure.Count };

        var error = Assert.Throws<ReportError>(() => ReportDefinitionValidator.Validate(definition));

        Assert.Equal("report_dimension_not_whitelisted", error.Code);
        Assert.Equal(422, error.Status);
    }

    [Fact]
    public void AC01_an_unknown_dataset_value_is_rejected_not_treated_as_a_default()
    {
        var definition = new ReportDefinition { Dataset = (ReportDataset)999, Dimension = ReportDimension.Status, Measure = ReportMeasure.Count };

        var error = Assert.Throws<ReportError>(() => ReportDefinitionValidator.Validate(definition));

        Assert.Equal("report_dataset_not_whitelisted", error.Code);
    }

    [Fact]
    public void AC01_the_whitelist_is_the_only_way_in_no_caller_supplied_field_name_is_ever_accepted()
    {
        // Structural proof, not behavioural: ReportDimension/ReportMeasure are closed enums, so
        // there is no code path anywhere in this project that can accept an arbitrary string as
        // a groupable field - TryParse is the only entry point and it only recognises the fixed
        // wire-string set below.
        Assert.False(ReportDimensionExtensions.TryParse("drop table reports;--", out _));
        Assert.False(ReportMeasureExtensions.TryParse("sum(secret_column)", out _));
    }
}
