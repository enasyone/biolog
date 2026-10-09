using Biolog.Features.MrzEngine.Model;
using Biolog.Features.MrzEngine.Tests.Fixtures;
using Xunit;

namespace Biolog.Features.MrzEngine.Tests;

/// <summary>Developer tests for BIO-DOC-002-AC02's "configured visa formats": MRV-A shares TD3's
/// 2x44 geometry but is resolved to a different, already-registered <see cref="MrzFormatDescriptor"/>
/// purely from the document-code character actually read (<c>V</c>), and carries no composite
/// check digit.</summary>
public sealed class MrzVisaFormatTests
{
    [Fact]
    public void AC02_a_44_column_block_with_a_v_document_code_resolves_to_mrv_a_not_td3()
    {
        var (line1, line2) = MrzSampleData.BuildMrvA();
        var canvas = new SyntheticRasterCanvas(width: 270, height: 40);
        canvas.DrawMrzLines([line1, line2], left: 3, top: 15);

        var outcome = MrzReader.Read(canvas.ToRaster());

        Assert.True(outcome.Detected);
        Assert.Equal(MrzFormatKind.MrvA, outcome.Format);
    }

    [Fact]
    public void mrv_a_carries_no_composite_check_digit_to_validate()
    {
        var (line1, line2) = MrzSampleData.BuildMrvA();
        var canvas = new SyntheticRasterCanvas(width: 270, height: 40);
        canvas.DrawMrzLines([line1, line2], left: 3, top: 15);

        var outcome = MrzReader.Read(canvas.ToRaster());

        Assert.False(outcome.CompositeCheckDigitApplicable);
        Assert.Null(outcome.CompositeCheckDigitValid);
    }

    [Fact]
    public void mrv_a_individual_field_check_digits_are_still_validated()
    {
        var (line1, line2) = MrzSampleData.BuildMrvA();
        var canvas = new SyntheticRasterCanvas(width: 270, height: 40);
        canvas.DrawMrzLines([line1, line2], left: 3, top: 15);

        var outcome = MrzReader.Read(canvas.ToRaster());

        Assert.False(outcome.RequiresManualReview);
        Assert.All(outcome.Fields.Where(f => f.HasCheckDigit), f => Assert.True(f.CheckDigitValid));
    }
}
