using Biolog.Features.DocumentVerify.Model;

namespace Biolog.Features.OcrEngine.Model;

public sealed record GenericOcrResult(IReadOnlyList<GenericTextBlock> Blocks);

/// <summary>
/// Reads arbitrary text blocks from a page with no template (BIO-DOC-004-AC02): detects text
/// lines via <see cref="OcrTextLineDetector"/> (real ink-projection analysis, not a fixed known
/// layout) and recognizes each one's raw text - never assigning a field type, never normalizing,
/// never claiming to know what the text means. That is the entire distinction from
/// <see cref="TemplateOcrEngine"/>.
/// </summary>
public static class GenericOcrEngine
{
    public static GenericOcrResult Read(DocumentRaster raster, IOcrLanguagePack languagePack)
    {
        var blocks = new List<GenericTextBlock>();

        foreach (var line in OcrTextLineDetector.DetectLines(raster))
        {
            var characters = new char[line.CellStartColumns.Count];
            var confidence = 1.0;

            for (var i = 0; i < line.CellStartColumns.Count; i++)
            {
                var cellLeft = line.CellStartColumns[i];
                var bitmap = new bool[OcrGlyphFont.GlyphHeight, OcrGlyphFont.GlyphWidth];
                for (var y = 0; y < OcrGlyphFont.GlyphHeight; y++)
                {
                    for (var x = 0; x < OcrGlyphFont.GlyphWidth; x++)
                    {
                        bitmap[y, x] = raster.HasInkAt(cellLeft + x, line.Top + y);
                    }
                }

                var (character, cellConfidence) = languagePack.MatchCell(bitmap);
                characters[i] = character;
                confidence = Math.Min(confidence, cellConfidence);
            }

            var left = line.CellStartColumns[0];
            var right = line.CellStartColumns[^1] + OcrGlyphFont.GlyphWidth;
            var boundingBox = BoundingBox.Create(
                (double)left / raster.Width,
                (double)line.Top / raster.Height,
                (double)(right - left) / raster.Width,
                (double)OcrGlyphFont.GlyphHeight / raster.Height);

            blocks.Add(new GenericTextBlock(new string(characters), boundingBox, confidence));
        }

        return new GenericOcrResult(blocks);
    }
}
