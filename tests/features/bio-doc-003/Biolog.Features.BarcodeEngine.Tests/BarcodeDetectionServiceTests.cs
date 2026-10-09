using Biolog.Features.BarcodeEngine.Model;
using Biolog.Features.BarcodeEngine.Tests.Fixtures;
using Xunit;

namespace Biolog.Features.BarcodeEngine.Tests;

/// <summary>Developer tests for BIO-DOC-003-AC01 (the orchestration over every registered
/// decoder) and AC03 (one hostile/misbehaving decoder must never take down the whole scan).</summary>
public sealed class BarcodeDetectionServiceTests
{
    [Fact]
    public void AC01_scans_a_code39_symbol_and_returns_it_as_a_decoded_result()
    {
        var canvas = new SyntheticBarcodeCanvas(width: 600, height: 60);
        canvas.DrawCode39("ABC123", left: 20, top: 15);
        var service = new BarcodeDetectionService(BarcodeDecoderRegistry.CreateDefault());

        var outcome = service.Scan(canvas.ToRaster());

        Assert.Contains(outcome.Decoded, r => r.Symbology == BarcodeSymbology.Code39);
    }

    [Fact]
    public void AC01_a_detected_but_undecoded_qr_symbol_is_reported_separately_from_decoded_results()
    {
        var canvas = new SyntheticBarcodeCanvas(width: 120, height: 120);
        canvas.DrawQrFinderPattern(left: 10, top: 10);
        canvas.DrawQrFinderPattern(left: 80, top: 10);
        canvas.DrawQrFinderPattern(left: 10, top: 80);
        var service = new BarcodeDetectionService(BarcodeDecoderRegistry.CreateDefault());

        var outcome = service.Scan(canvas.ToRaster());

        Assert.DoesNotContain(outcome.Decoded, r => r.Symbology == BarcodeSymbology.Qr);
        Assert.Contains(outcome.UndecodedDetections, a => a.Symbology == BarcodeSymbology.Qr);
    }

    [Fact]
    public void AC03_a_decoder_that_throws_is_isolated_and_never_aborts_the_rest_of_the_scan()
    {
        var canvas = new SyntheticBarcodeCanvas(width: 400, height: 60);
        canvas.DrawCode39("SAFE", left: 20, top: 15);

        var registry = new BarcodeDecoderRegistry();
        registry.Register(new ThrowingDecoder());
        registry.Register(new Code39Decoder());

        var service = new BarcodeDetectionService(registry);

        var outcome = service.Scan(canvas.ToRaster());

        Assert.Contains(outcome.Decoded, r => r.Symbology == BarcodeSymbology.Code39);
        Assert.DoesNotContain(outcome.Decoded, r => r.Symbology == BarcodeSymbology.Aztec);
        Assert.DoesNotContain(outcome.UndecodedDetections, a => a.Symbology == BarcodeSymbology.Aztec);
    }

    [Fact]
    public void AC03_a_blank_raster_scans_cleanly_with_no_results_and_no_exception()
    {
        var service = new BarcodeDetectionService(BarcodeDecoderRegistry.CreateDefault());
        var raster = BarcodeRaster.Blank(width: 300, height: 300);

        var exception = Record.Exception(() => service.Scan(raster));

        Assert.Null(exception);
        var outcome = service.Scan(raster);
        Assert.Empty(outcome.Decoded);
        Assert.Empty(outcome.UndecodedDetections);
    }

    private sealed class ThrowingDecoder : IBarcodeDecoder
    {
        public BarcodeSymbology Symbology => BarcodeSymbology.Aztec;

        public BarcodeDecodeAttempt TryDecode(BarcodeRaster raster) =>
            throw new InvalidOperationException("simulated hostile-input crash");
    }
}
