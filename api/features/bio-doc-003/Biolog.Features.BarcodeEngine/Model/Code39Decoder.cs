using Biolog.Features.DocumentVerify.Model;

namespace Biolog.Features.BarcodeEngine.Model;

/// <summary>
/// A complete, native Code 39 decoder (BIO-DOC-003-AC01/AC02/AC03): detects a 1D bar-code region
/// by its row-height signature, reads the ordered sequence of bar/space pixel runs, classifies
/// each as narrow or wide against the fixed, known module pitch this engine renders at (no
/// run-length estimation needed - real-world noisy-scan tolerance is explicitly out of scope for
/// this native reference implementation, see the design doc), matches each 9-element chunk
/// against <see cref="Code39Table.Patterns"/>, and requires valid <c>*</c> start/stop framing.
/// Any run width, chunk shape or character that does not cleanly match is treated as "no valid
/// symbol here" (AC03) - never a partial or best-guess payload.
/// </summary>
public sealed class Code39Decoder : IBarcodeDecoder
{
    public const int ModulePx = 4;
    public const int BarHeightPx = 30;

    public BarcodeSymbology Symbology => BarcodeSymbology.Code39;

    public BarcodeDecodeAttempt TryDecode(BarcodeRaster raster)
    {
        foreach (var (top, height) in RasterScanning.FindRowBands(raster))
        {
            if (height != BarHeightPx)
            {
                continue;
            }

            var attempt = TryDecodeBand(raster, top, height);
            if (attempt is not null)
            {
                return attempt;
            }
        }

        return BarcodeDecodeAttempt.NotDetected(Symbology);
    }

    private BarcodeDecodeAttempt? TryDecodeBand(BarcodeRaster raster, int top, int height)
    {
        var runs = RasterScanning.FindColumnRuns(raster, top, height);
        var firstInk = runs.FindIndex(r => r.Ink);
        if (firstInk < 0)
        {
            return null;
        }

        var starts = new int[runs.Count + 1];
        for (var i = 0; i < runs.Count; i++)
        {
            starts[i + 1] = starts[i] + runs[i].Width;
        }

        var characters = new List<char>();
        var index = firstInk;
        var symbolLeft = starts[firstInk];
        var symbolRight = symbolLeft;

        while (index + Code39Table.ElementsPerCharacter <= runs.Count)
        {
            if (!IsAlternatingBarSpace(runs, index))
            {
                break;
            }

            var pattern = ClassifyWidths(runs, index);
            if (pattern is null)
            {
                break;
            }

            var character = MatchCharacter(pattern);
            if (character is null)
            {
                break;
            }

            characters.Add(character.Value);
            index += Code39Table.ElementsPerCharacter;
            symbolRight = starts[index];

            if (index < runs.Count && !runs[index].Ink)
            {
                index += 1; // the narrow inter-character gap, not part of either character's own pattern.
            }
        }

        if (characters.Count < 2 || characters[0] != Code39Table.StartStop || characters[^1] != Code39Table.StartStop)
        {
            // No valid start/stop framing - malformed input or simply not a Code 39 symbol.
            return null;
        }

        var payloadChars = characters.GetRange(1, characters.Count - 2);
        var payload = System.Text.Encoding.ASCII.GetBytes(new string(payloadChars.ToArray()));

        var boundingBox = BoundingBox.Create(
            (double)symbolLeft / raster.Width,
            (double)top / raster.Height,
            (double)(symbolRight - symbolLeft) / raster.Width,
            (double)height / raster.Height);

        return BarcodeDecodeAttempt.Decode(Symbology, boundingBox, payload, confidence: 1.0);
    }

    private static bool IsAlternatingBarSpace(List<(bool Ink, int Width)> runs, int startIndex)
    {
        for (var i = 0; i < Code39Table.ElementsPerCharacter; i++)
        {
            if (runs[startIndex + i].Ink != (i % 2 == 0))
            {
                return false;
            }
        }

        return true;
    }

    private static bool[]? ClassifyWidths(List<(bool Ink, int Width)> runs, int startIndex)
    {
        var pattern = new bool[Code39Table.ElementsPerCharacter];
        for (var i = 0; i < Code39Table.ElementsPerCharacter; i++)
        {
            var moduleCount = (double)runs[startIndex + i].Width / ModulePx;
            if (moduleCount is >= 0.6 and <= 1.4)
            {
                pattern[i] = false;
            }
            else if (moduleCount is >= 1.6 and <= 2.4)
            {
                pattern[i] = true;
            }
            else
            {
                return null; // neither ~1 nor ~2 modules - ambiguous/malformed, AC03 inert data.
            }
        }

        return pattern;
    }

    private static char? MatchCharacter(bool[] pattern)
    {
        foreach (var (character, candidate) in Code39Table.Patterns)
        {
            if (candidate.SequenceEqual(pattern))
            {
                return character;
            }
        }

        return null;
    }
}
