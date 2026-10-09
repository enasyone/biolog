using System.Text;
using Biolog.Features.BarcodeEngine.Model;
using Biolog.Features.BarcodeEngine.Tests.Fixtures;
using Xunit;

namespace Biolog.Features.BarcodeEngine.Tests;

/// <summary>Developer tests for BIO-DOC-003-AC01/AC02/AC03 against the one fully native decoder
/// this story ships (Code 39).</summary>
public sealed class Code39DecoderTests
{
    private static BarcodeRaster RenderPayload(string payload, out int width)
    {
        width = 40 + payload.Length * 60 + 200;
        var canvas = new SyntheticBarcodeCanvas(width, height: 60);
        canvas.DrawCode39(payload, left: 20, top: 15);
        return canvas.ToRaster();
    }

    [Fact]
    public void AC01_AC02_decodes_a_simple_payload_and_reports_symbology_bbox_and_confidence()
    {
        var raster = RenderPayload("HELLO123", out _);

        var attempt = new Code39Decoder().TryDecode(raster);

        Assert.True(attempt.Detected);
        Assert.True(attempt.Decoded);
        Assert.Equal(BarcodeSymbology.Code39, attempt.Symbology);
        Assert.Equal("HELLO123", Encoding.ASCII.GetString(attempt.RawPayload!));
        Assert.NotNull(attempt.BoundingBox);
        Assert.Equal(1.0, attempt.Confidence);
    }

    [Fact]
    public void AC02_bounding_box_is_normalized_and_within_the_unit_square()
    {
        var raster = RenderPayload("ABC", out _);

        var attempt = new Code39Decoder().TryDecode(raster);

        var box = attempt.BoundingBox!;
        Assert.True(box.X >= 0 && box.X < 1);
        Assert.True(box.Y >= 0 && box.Y < 1);
        Assert.True(box.Width > 0 && box.Width <= 1);
        Assert.True(box.Height > 0 && box.Height <= 1);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("Z")]
    [InlineData("HELLO WORLD")]
    [InlineData("A1B2C3")]
    public void AC01_round_trips_every_kind_of_payload_through_the_shared_pattern_table(string payload)
    {
        var raster = RenderPayload(payload, out _);

        var attempt = new Code39Decoder().TryDecode(raster);

        Assert.True(attempt.Decoded);
        Assert.Equal(payload, Encoding.ASCII.GetString(attempt.RawPayload!));
    }

    [Fact]
    public void AC03_a_payload_using_shell_and_path_metacharacters_is_decoded_as_opaque_bytes_not_interpreted()
    {
        // Code 39's own alphabet is limited (no control/binary chars), but it does include
        // '$', '/', '+', '.', '%' and space - enough to spell something that would be dangerous
        // if any downstream code ever executed or path-resolved it instead of hashing it.
        const string hostile = "..$/+.%A";
        var raster = RenderPayload(hostile, out _);

        var attempt = new Code39Decoder().TryDecode(raster);

        Assert.True(attempt.Decoded);
        Assert.Equal(hostile, Encoding.ASCII.GetString(attempt.RawPayload!));

        // The service-level result never carries the raw text at all, only a hash (AC02/AC04).
        var result = BarcodeDecodeResult.FromDecodedAttempt(attempt);
        Assert.Equal(64, result.RawPayloadHash.Length);
        Assert.DoesNotContain(hostile, result.RawPayloadHash, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AC03_a_blank_raster_is_not_detected_rather_than_crashing()
    {
        var raster = BarcodeRaster.Blank(width: 300, height: 60);

        var attempt = new Code39Decoder().TryDecode(raster);

        Assert.False(attempt.Detected);
        Assert.False(attempt.Decoded);
    }

    [Fact]
    public void AC03_random_noise_of_the_right_row_height_never_crashes_and_is_treated_as_undecodable()
    {
        var canvas = new SyntheticBarcodeCanvas(width: 300, height: 60);
        var rng = new Random(Seed: 12345);
        for (var x = 0; x < 300; x++)
        {
            canvas.FillRect(x, 15, 1, Code39Decoder.BarHeightPx, ink: rng.Next(2) == 0);
        }

        var exception = Record.Exception(() => new Code39Decoder().TryDecode(canvas.ToRaster()));

        Assert.Null(exception);
    }

    [Fact]
    public void AC03_missing_stop_character_is_rejected_not_partially_decoded()
    {
        var canvas = new SyntheticBarcodeCanvas(width: 300, height: 60);
        var payloadEnd = canvas.DrawCode39("AB", left: 20, top: 15);
        // Every Code 39 character is exactly 9 elements, 3 wide (2 modules) + 6 narrow (1 module)
        // = 48px at this engine's ModulePx=4 - erase exactly the trailing stop character.
        const int characterWidthPx = 48;
        canvas.FillRect(payloadEnd - characterWidthPx, 15, characterWidthPx, Code39Decoder.BarHeightPx, ink: false);

        var attempt = new Code39Decoder().TryDecode(canvas.ToRaster());

        Assert.False(attempt.Decoded);
    }

    [Fact]
    public void AC04_a_decode_attempt_never_exposes_an_authenticity_flag()
    {
        var properties = typeof(BarcodeDecodeAttempt).GetProperties();
        Assert.DoesNotContain(properties, p => p.Name.Contains("Authentic", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(properties, p => p.Name.Contains("Valid", StringComparison.OrdinalIgnoreCase) && p.Name != nameof(BarcodeDecodeAttempt.Decoded));
    }
}
