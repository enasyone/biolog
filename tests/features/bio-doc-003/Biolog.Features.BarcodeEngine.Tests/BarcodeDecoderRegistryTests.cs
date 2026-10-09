using Biolog.Features.BarcodeEngine.Model;
using Xunit;

namespace Biolog.Features.BarcodeEngine.Tests;

/// <summary>Developer tests for BIO-DOC-003-AC01's "replaceable decoder interfaces": all six
/// required symbologies are registered by default, and any one can be swapped without touching
/// the service above it.</summary>
public sealed class BarcodeDecoderRegistryTests
{
    [Fact]
    public void CreateDefault_registers_all_six_required_symbologies()
    {
        var registry = BarcodeDecoderRegistry.CreateDefault();

        var registered = registry.All.Select(d => d.Symbology).ToHashSet();
        var required = new HashSet<BarcodeSymbology>
        {
            BarcodeSymbology.Qr,
            BarcodeSymbology.Pdf417,
            BarcodeSymbology.DataMatrix,
            BarcodeSymbology.Aztec,
            BarcodeSymbology.Code128,
            BarcodeSymbology.Code39,
        };

        Assert.Equal(required, registered);
    }

    [Fact]
    public void Register_replaces_any_prior_decoder_for_the_same_symbology()
    {
        var registry = new BarcodeDecoderRegistry();
        var original = new StubDecoder(BarcodeSymbology.Code39, marker: "original");
        var replacement = new StubDecoder(BarcodeSymbology.Code39, marker: "replacement");

        registry.Register(original);
        registry.Register(replacement);

        Assert.Same(replacement, registry.Get(BarcodeSymbology.Code39));
        Assert.Single(registry.All);
    }

    [Fact]
    public void Get_returns_null_for_an_unregistered_symbology()
    {
        var registry = new BarcodeDecoderRegistry();
        Assert.Null(registry.Get(BarcodeSymbology.Aztec));
    }

    private sealed class StubDecoder(BarcodeSymbology symbology, string marker) : IBarcodeDecoder
    {
        public string Marker { get; } = marker;
        public BarcodeSymbology Symbology { get; } = symbology;
        public BarcodeDecodeAttempt TryDecode(BarcodeRaster raster) => BarcodeDecodeAttempt.NotDetected(Symbology);
    }
}
