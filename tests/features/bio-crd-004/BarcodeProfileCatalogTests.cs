// Developer tests for BIO-CRD-004. SYNTHETIC fixtures only.
using Biolog.Features.CardObjects.Domain;
using Xunit;

namespace Biolog.Features.CardObjects.Tests;

public sealed class BarcodeProfileCatalogTests
{
    [Fact]
    public void AC01_the_default_profile_is_alphanumeric()
    {
        Assert.Equal(BarcodeProfile.Alphanumeric, BarcodeProfileCatalog.DefaultProfile);
        Assert.True(BarcodeProfileCatalog.IsDefault(BarcodeProfile.Alphanumeric));
    }

    [Theory]
    [InlineData(BarcodeProfile.Qr)]
    [InlineData(BarcodeProfile.Pdf417)]
    [InlineData(BarcodeProfile.DataMatrix)]
    [InlineData(BarcodeProfile.Aztec)]
    public void AC02_every_2D_symbology_is_optional_never_default(BarcodeProfile profile)
    {
        Assert.Contains(profile, BarcodeProfileCatalog.OptionalProfiles);
        Assert.False(BarcodeProfileCatalog.IsDefault(profile));
    }

    [Fact]
    public void AC02_the_optional_list_names_exactly_QR_PDF417_DataMatrix_and_Aztec()
    {
        Assert.Equal(4, BarcodeProfileCatalog.OptionalProfiles.Count);
        Assert.Equal(
            [BarcodeProfile.Qr, BarcodeProfile.Pdf417, BarcodeProfile.DataMatrix, BarcodeProfile.Aztec],
            BarcodeProfileCatalog.OptionalProfiles);
    }

    [Theory]
    [InlineData(BarcodeProfile.Alphanumeric, "alphanumeric")]
    [InlineData(BarcodeProfile.Qr, "qr")]
    [InlineData(BarcodeProfile.Pdf417, "pdf417")]
    [InlineData(BarcodeProfile.DataMatrix, "data_matrix")]
    [InlineData(BarcodeProfile.Aztec, "aztec")]
    public void Wire_form_round_trips(BarcodeProfile profile, string wire)
    {
        Assert.Equal(wire, profile.ToWireString());
        Assert.True(BarcodeProfileExtensions.TryParse(wire, out var parsed));
        Assert.Equal(profile, parsed);
    }

    [Fact]
    public void An_unrecognised_wire_value_fails_to_parse()
    {
        Assert.False(BarcodeProfileExtensions.TryParse("code39", out _));
    }
}
