using Biolog.Features.MrzEngine.Model;
using Biolog.Features.MrzEngine.Tests.Fixtures;
using Xunit;

namespace Biolog.Features.MrzEngine.Tests;

/// <summary>Developer tests for BIO-DOC-002-AC02/AC03/AC04 against ICAO's own published TD3
/// worked example, rendered through the full detect -> recognize -> parse -> validate pipeline
/// (<see cref="MrzReader.Read"/>), not just the check-digit math in isolation.</summary>
public sealed class MrzTd3ParserTests
{
    private static MrzReadOutcome ReadSample(string line1, string line2)
    {
        var canvas = new SyntheticRasterCanvas(width: 270, height: 40);
        canvas.DrawMrzLines([line1, line2], left: 3, top: 15);
        return MrzReader.Read(canvas.ToRaster());
    }

    [Fact]
    public void AC02_parses_every_td3_field_from_the_icao_worked_example()
    {
        var outcome = ReadSample(MrzSampleData.Td3Line1, MrzSampleData.Td3Line2);

        Assert.True(outcome.Detected);
        Assert.Equal(MrzFormatKind.Td3, outcome.Format);

        var byName = outcome.Fields.ToDictionary(f => f.FieldName);
        Assert.Equal("UTO", byName["issuing_state"].RawValue);
        Assert.Equal("L898902C3", byName["document_number"].RawValue);
        Assert.Equal("UTO", byName["nationality"].RawValue);
        Assert.Equal("740812", byName["date_of_birth"].RawValue);
        Assert.Equal("F", byName["sex"].RawValue);
        Assert.Equal("120415", byName["expiry"].RawValue);
        Assert.Equal("ZE184226B<<<<<", byName["personal_number"].RawValue);
    }

    [Fact]
    public void AC04_preserves_raw_alongside_a_readable_normalized_value()
    {
        var outcome = ReadSample(MrzSampleData.Td3Line1, MrzSampleData.Td3Line2);
        var byName = outcome.Fields.ToDictionary(f => f.FieldName);

        Assert.Equal("ERIKSSON<<ANNA<MARIA<<<<<<<<<<<<<<<<<<<", byName["name"].RawValue);
        Assert.Equal("ERIKSSON, ANNA MARIA", byName["name"].NormalizedValue);

        Assert.Equal("740812", byName["date_of_birth"].RawValue);
        Assert.Equal("74-08-12", byName["date_of_birth"].NormalizedValue);

        Assert.Equal("F", byName["sex"].RawValue);
        Assert.Equal("female", byName["sex"].NormalizedValue);
    }

    [Fact]
    public void AC03_every_individual_and_composite_check_digit_matches_for_a_valid_sample()
    {
        var outcome = ReadSample(MrzSampleData.Td3Line1, MrzSampleData.Td3Line2);

        var checked_ = outcome.Fields.Where(f => f.HasCheckDigit).ToList();
        Assert.Equal(4, checked_.Count); // document_number, date_of_birth, expiry, personal_number
        Assert.All(checked_, f => Assert.True(f.CheckDigitValid));

        Assert.True(outcome.CompositeCheckDigitApplicable);
        Assert.True(outcome.CompositeCheckDigitValid);
    }

    [Fact]
    public void AC05_a_fully_valid_sample_never_requires_manual_review()
    {
        var outcome = ReadSample(MrzSampleData.Td3Line1, MrzSampleData.Td3Line2);

        Assert.False(outcome.RequiresManualReview);
        Assert.Empty(outcome.ReviewReasons);
    }

    [Fact]
    public void AC03_AC05_a_mismatched_check_digit_is_reported_not_silently_corrected()
    {
        // Document number check digit deliberately wrong (6 -> 5); everything else unchanged.
        var corruptedLine2 = "L898902C35UTO7408122F1204159ZE184226B<<<<<10";

        var outcome = ReadSample(MrzSampleData.Td3Line1, corruptedLine2);

        var docNumber = outcome.Fields.Single(f => f.FieldName == "document_number");
        Assert.Equal("L898902C3", docNumber.RawValue); // preserved exactly as read, not "fixed"
        Assert.Equal('5', docNumber.ReadCheckDigit);
        Assert.Equal(6, docNumber.ComputedCheckDigit);
        Assert.False(docNumber.CheckDigitValid);

        Assert.True(outcome.RequiresManualReview);
        Assert.Contains("check_digit_mismatch:document_number", outcome.ReviewReasons);
        // The composite check digit was computed over the (now-wrong) read document number check
        // digit too, so it necessarily also disagrees - both are reported, neither is hidden.
        Assert.Contains("composite_check_digit_mismatch", outcome.ReviewReasons);
    }

    [Fact]
    public void AC01_reports_a_normalized_region_bounding_box_spanning_both_lines()
    {
        var outcome = ReadSample(MrzSampleData.Td3Line1, MrzSampleData.Td3Line2);

        Assert.NotNull(outcome.Region);
        Assert.True(outcome.Region!.X >= 0 && outcome.Region.X < 1);
        Assert.True(outcome.Region.Y >= 0 && outcome.Region.Y < 1);
        Assert.True(outcome.Region.Width > 0 && outcome.Region.Width <= 1);
        Assert.True(outcome.Region.Height > 0 && outcome.Region.Height <= 1);
    }

    [Fact]
    public void every_field_carries_its_own_bounding_box_within_the_region()
    {
        var outcome = ReadSample(MrzSampleData.Td3Line1, MrzSampleData.Td3Line2);

        Assert.All(outcome.Fields, f =>
        {
            Assert.True(f.BoundingBox.X >= outcome.Region!.X - 1e-9);
            Assert.True(f.BoundingBox.Y >= outcome.Region.Y - 1e-9);
        });
    }
}
