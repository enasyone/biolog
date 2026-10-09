using Biolog.Features.DocumentVerify.Model;

namespace Biolog.Features.OcrEngine.Model;

/// <summary>
/// Reads a document's fields from a known <see cref="OcrDocumentTemplate"/> (BIO-DOC-004-AC01).
/// Because a template already declares exactly where each field sits, this engine needs no
/// dynamic text-line detection (unlike <see cref="GenericOcrEngine"/>) - it slices the declared
/// pixel region directly into glyph cells at this engine's fixed pitch, the same "known layout,
/// no detection needed" shape BIO-DOC-002's MRZ field extraction used.
/// </summary>
public static class TemplateOcrEngine
{
    /// <summary>A critical field read below this confidence requires manual review (AC04).
    /// Non-critical fields never gate review regardless of confidence - AC04 names "critical
    /// fields" specifically, not every field.</summary>
    public const double MinAcceptableConfidence = 0.85;

    public static TemplateOcrResult Read(DocumentRaster raster, OcrDocumentTemplate template, IOcrLanguagePack languagePack)
    {
        var fields = new List<OcrFieldRead>(template.Fields.Count);
        var reviewReasons = new List<string>();

        foreach (var fieldSpec in template.Fields)
        {
            var confidence = 1.0;
            var characters = new char[fieldSpec.CharacterCount];

            for (var i = 0; i < fieldSpec.CharacterCount; i++)
            {
                var cellLeft = fieldSpec.Left + i * OcrGlyphFont.CellPitchX;
                var bitmap = new bool[OcrGlyphFont.GlyphHeight, OcrGlyphFont.GlyphWidth];
                for (var y = 0; y < OcrGlyphFont.GlyphHeight; y++)
                {
                    for (var x = 0; x < OcrGlyphFont.GlyphWidth; x++)
                    {
                        bitmap[y, x] = raster.HasInkAt(cellLeft + x, fieldSpec.Top + y);
                    }
                }

                var (character, cellConfidence) = languagePack.MatchCell(bitmap);
                characters[i] = character;
                confidence = Math.Min(confidence, cellConfidence);
            }

            var rawValue = new string(characters);
            var normalizedValue = Normalize(fieldSpec.FieldType, rawValue);

            var widthPx = fieldSpec.CharacterCount * OcrGlyphFont.CellPitchX - 1;
            var boundingBox = BoundingBox.Create(
                (double)fieldSpec.Left / raster.Width,
                (double)fieldSpec.Top / raster.Height,
                (double)widthPx / raster.Width,
                (double)OcrGlyphFont.GlyphHeight / raster.Height);

            fields.Add(new OcrFieldRead(
                fieldSpec.FieldName, fieldSpec.FieldType, rawValue, normalizedValue, confidence,
                boundingBox, fieldSpec.Critical));

            if (fieldSpec.Critical && confidence < MinAcceptableConfidence)
            {
                reviewReasons.Add($"low_confidence:{fieldSpec.FieldName}");
            }
        }

        return new TemplateOcrResult(fields, reviewReasons.Count > 0, reviewReasons);
    }

    private static string Normalize(OcrFieldType fieldType, string raw) => fieldType switch
    {
        OcrFieldType.DateOfBirth or OcrFieldType.Expiry => NormalizeDate(raw),
        OcrFieldType.Sex => raw.Trim() switch { "M" => "male", "F" => "female", _ => "unspecified" },
        _ => raw.Trim(),
    };

    private static string NormalizeDate(string raw)
    {
        var trimmed = raw.Trim();
        return trimmed.Length == 6 ? $"{trimmed[..2]}-{trimmed[2..4]}-{trimmed[4..6]}" : trimmed;
    }
}
