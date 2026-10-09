namespace Biolog.Features.MrzEngine.Model;

/// <summary>One recognized character cell: what <see cref="MrzGlyphFont.MatchCell"/> returned,
/// plus where the cell sits in the raster (used to build a per-field <see cref="BoundingBox"/>
/// later).</summary>
public sealed record MrzRecognizedCell(char Character, double Confidence, int Left, int Top);

/// <summary>
/// Slices the region a <see cref="MrzRegionDetector"/> detected into individual glyph cells and
/// recognizes each one against <see cref="MrzGlyphFont"/> (BIO-DOC-002-AC01/AC04: raw recognition
/// values are what feed the parser's "raw" field values, before any normalization). Every cell is
/// always assigned its single nearest-template character - this type never declines to produce a
/// character for a low-confidence cell; AC05's "requires review, not a guessed correction" is
/// enforced by the parser/validator reading this recognizer's confidence numbers, not by this
/// type silently omitting or substituting a value.
/// </summary>
public static class MrzGlyphRecognizer
{
    public static IReadOnlyList<MrzRecognizedCell> RecognizeLine(DocumentRaster raster, MrzDetectedLine line)
    {
        var cells = new List<MrzRecognizedCell>(line.CellStartColumns.Count);
        foreach (var left in line.CellStartColumns)
        {
            var bitmap = new bool[MrzGlyphFont.GlyphHeight, MrzGlyphFont.GlyphWidth];
            for (var y = 0; y < MrzGlyphFont.GlyphHeight; y++)
            {
                for (var x = 0; x < MrzGlyphFont.GlyphWidth; x++)
                {
                    bitmap[y, x] = raster.HasInkAt(left + x, line.Top + y);
                }
            }

            var (character, confidence) = MrzGlyphFont.MatchCell(bitmap);
            cells.Add(new MrzRecognizedCell(character, confidence, left, line.Top));
        }

        return cells;
    }
}
