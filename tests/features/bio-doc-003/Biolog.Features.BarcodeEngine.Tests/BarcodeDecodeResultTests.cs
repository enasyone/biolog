using System.Reflection;
using Biolog.Features.BarcodeEngine.Model;
using Biolog.Features.BarcodeEngine.Tests.Fixtures;
using Xunit;

namespace Biolog.Features.BarcodeEngine.Tests;

/// <summary>Developer tests for BIO-DOC-003-AC02 (exactly symbology/bbox/hash/confidence, nothing
/// else) and AC04 (no authenticity flag anywhere on the result).</summary>
public sealed class BarcodeDecodeResultTests
{
    [Fact]
    public void AC02_result_carries_exactly_symbology_bbox_hash_and_confidence()
    {
        var propertyNames = typeof(BarcodeDecodeResult)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToHashSet();

        Assert.Equal(
            new HashSet<string> { "Symbology", "BoundingBox", "RawPayloadHash", "Confidence" },
            propertyNames);
    }

    [Fact]
    public void AC02_hashes_the_raw_payload_and_never_carries_the_payload_itself()
    {
        var canvas = new SyntheticBarcodeCanvas(width: 600, height: 60);
        canvas.DrawCode39("SECRET", left: 20, top: 15);
        var attempt = new Code39Decoder().TryDecode(canvas.ToRaster());

        var result = BarcodeDecodeResult.FromDecodedAttempt(attempt);

        Assert.Equal(64, result.RawPayloadHash.Length);
        Assert.True(result.RawPayloadHash.All(Uri.IsHexDigit));
        Assert.True(result.RawPayloadHash.All(c => !char.IsUpper(c)));
    }

    [Fact]
    public void AC02_the_same_payload_always_hashes_to_the_same_value()
    {
        var canvas1 = new SyntheticBarcodeCanvas(width: 600, height: 60);
        canvas1.DrawCode39("REPEAT", left: 20, top: 15);
        var canvas2 = new SyntheticBarcodeCanvas(width: 650, height: 60);
        canvas2.DrawCode39("REPEAT", left: 40, top: 15);

        var result1 = BarcodeDecodeResult.FromDecodedAttempt(new Code39Decoder().TryDecode(canvas1.ToRaster()));
        var result2 = BarcodeDecodeResult.FromDecodedAttempt(new Code39Decoder().TryDecode(canvas2.ToRaster()));

        Assert.Equal(result1.RawPayloadHash, result2.RawPayloadHash);
    }

    [Fact]
    public void FromDecodedAttempt_refuses_an_attempt_that_was_never_actually_decoded()
    {
        var notDetected = BarcodeDecodeAttempt.NotDetected(BarcodeSymbology.Code39);
        Assert.Throws<ArgumentException>(() => BarcodeDecodeResult.FromDecodedAttempt(notDetected));
    }

    [Fact]
    public void AC04_no_property_anywhere_on_the_result_or_attempt_types_suggests_authenticity()
    {
        foreach (var type in new[] { typeof(BarcodeDecodeResult), typeof(BarcodeDecodeAttempt) })
        {
            var suspicious = type.GetProperties()
                .Where(p => p.Name.Contains("Authentic", StringComparison.OrdinalIgnoreCase)
                    || p.Name.Contains("Genuine", StringComparison.OrdinalIgnoreCase)
                    || p.Name.Contains("Trusted", StringComparison.OrdinalIgnoreCase))
                .ToList();
            Assert.Empty(suspicious);
        }
    }
}
