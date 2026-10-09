// Developer tests for BIO-CRD-001-AC04. SYNTHETIC fixtures only.
using System.Text;
using Biolog.Api.Features.BrandAssets;
using Xunit;

namespace Biolog.UnitTests.Features.BrandAssets;

public sealed class SvgValidatorTests
{
    [Fact]
    public void Accepts_a_well_formed_svg_with_no_active_content()
    {
        var result = SvgValidator.Validate(Encoding.UTF8.GetBytes(SyntheticImages.ValidSvg));

        Assert.True(result.IsValid);
        Assert.Empty(result.Messages);
    }

    [Fact]
    public void Rejects_malformed_xml()
    {
        var result = SvgValidator.Validate(Encoding.UTF8.GetBytes("<svg><rect></svg>"));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Rejects_a_non_svg_root_element()
    {
        var result = SvgValidator.Validate(Encoding.UTF8.GetBytes("<root><svg/></root>"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Messages, m => m.Contains("root element"));
    }

    [Theory]
    [InlineData(nameof(SyntheticImages.SvgWithScriptTag), "script")]
    [InlineData(nameof(SyntheticImages.SvgWithEventHandler), "event-handler")]
    [InlineData(nameof(SyntheticImages.SvgWithJavascriptUri), "javascript:")]
    [InlineData(nameof(SyntheticImages.SvgWithForeignObject), "foreignObject")]
    public void Rejects_active_content(string fixtureName, string expectedFragment)
    {
        var svg = fixtureName switch
        {
            nameof(SyntheticImages.SvgWithScriptTag) => SyntheticImages.SvgWithScriptTag,
            nameof(SyntheticImages.SvgWithEventHandler) => SyntheticImages.SvgWithEventHandler,
            nameof(SyntheticImages.SvgWithJavascriptUri) => SyntheticImages.SvgWithJavascriptUri,
            nameof(SyntheticImages.SvgWithForeignObject) => SyntheticImages.SvgWithForeignObject,
            _ => throw new ArgumentOutOfRangeException(nameof(fixtureName)),
        };

        var result = SvgValidator.Validate(Encoding.UTF8.GetBytes(svg));

        Assert.False(result.IsValid);
        Assert.Contains(result.Messages, m => m.Contains(expectedFragment, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Rejects_a_doctype_declaration_outright_including_an_xxe_attempt()
    {
        var result = SvgValidator.Validate(Encoding.UTF8.GetBytes(SyntheticImages.SvgWithXxeDoctype));

        Assert.False(result.IsValid);
    }
}
