using Biolog.Features.OcrEngine.Model;

namespace Biolog.Features.DocumentClassifier.Tests.Fixtures;

/// <summary>
/// Own local copy of BIO-DOC-004's identically named fixture: builds a deliberately noisy - but
/// still nearest-match-correct - glyph cell bitmap, letting this story's ranking tests produce a
/// real, sub-1.0 OCR confidence to rank against. Self-verifying against the real production
/// matcher (greedily flips pixels, keeping only ones that leave <see cref="OcrGlyphFont.MatchCell"/>'s
/// nearest match unchanged) rather than an assumption about it.
/// </summary>
public static class GlyphCorruption
{
    public static bool[,] LowerConfidenceButKeepCharacter(char character, int flipCount)
    {
        var bitmap = (bool[,])OcrGlyphFont.Glyphs[character].Clone();
        var flipped = 0;

        for (var y = 0; y < OcrGlyphFont.GlyphHeight && flipped < flipCount; y++)
        {
            for (var x = 0; x < OcrGlyphFont.GlyphWidth && flipped < flipCount; x++)
            {
                bitmap[y, x] = !bitmap[y, x];
                var (match, _) = OcrGlyphFont.MatchCell(bitmap);
                if (match == character)
                {
                    flipped++;
                }
                else
                {
                    bitmap[y, x] = !bitmap[y, x];
                }
            }
        }

        if (flipped < flipCount)
        {
            throw new InvalidOperationException(
                $"could only safely flip {flipped}/{flipCount} pixels for '{character}' without changing its nearest match");
        }

        return bitmap;
    }
}
