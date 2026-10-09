using Biolog.Features.OcrEngine.Model;

namespace Biolog.Features.DocumentTemplateEditor.Domain;

/// <summary>
/// AC03's actual corpus runner. Builds a synthetic <see cref="DocumentRaster"/> for one
/// <see cref="DocumentTemplateCorpusSample"/> by stamping each field region's own glyph
/// characters (<see cref="OcrGlyphFont.Glyphs"/> - the same bitmap templates
/// <c>TemplateOcrEngine</c> itself matches against, BIO-DOC-004's own public, dependency-free
/// font), then runs the REAL <see cref="TemplateOcrEngine.Read"/> against it and compares every
/// field's own raw read back to the sample's declared expectation. Synthetic data only (no image
/// library exists anywhere in this repo) - and because the renderer and the reader share the
/// exact same glyph font, a correctly defined template always passes its own honestly-described
/// corpus; the only way to fail is a genuine mismatch between a region's declared geometry/
/// character count and what the sample actually expects there, or a deliberately
/// <see cref="DocumentTemplateCorpusSample.CorruptFieldNames"/>-marked field.
/// </summary>
internal static class SyntheticCorpusRenderer
{
    public static DocumentTemplateCorpusRunResult Run(
        DocumentTemplateReferenceLayout layout,
        IReadOnlyList<OcrTemplateField> fieldRegions,
        IReadOnlyList<DocumentTemplateCorpusSample> samples,
        string now)
    {
        if (samples.Count == 0)
        {
            throw DocumentTemplateDraftError.EmptyCorpus();
        }

        var template = new OcrDocumentTemplate("draft-corpus-run", fieldRegions);
        var languagePack = new EnglishLanguagePack();
        var failures = new List<string>();

        foreach (var sample in samples)
        {
            var raster = Render(layout, fieldRegions, sample);
            var result = TemplateOcrEngine.Read(raster, template, languagePack);

            foreach (var fieldRead in result.Fields)
            {
                var expected = sample.ExpectedFieldValues[fieldRead.FieldName].Trim();
                var actual = fieldRead.RawValue.Trim();
                if (!string.Equals(expected, actual, StringComparison.Ordinal))
                {
                    failures.Add($"{sample.SampleId}:{fieldRead.FieldName}:expected='{expected}':actual='{actual}'");
                }
            }

            if (result.RequiresManualReview)
            {
                failures.Add($"{sample.SampleId}:requires_manual_review:{string.Join(',', result.ReviewReasons)}");
            }
        }

        return new DocumentTemplateCorpusRunResult(failures.Count == 0, samples.Count, failures, now);
    }

    private static DocumentRaster Render(DocumentTemplateReferenceLayout layout, IReadOnlyList<OcrTemplateField> fieldRegions, DocumentTemplateCorpusSample sample)
    {
        var ink = new bool[layout.Height, layout.Width];

        foreach (var field in fieldRegions)
        {
            if (!sample.ExpectedFieldValues.TryGetValue(field.FieldName, out var expected))
            {
                throw DocumentTemplateDraftError.MissingExpectedValue(sample.SampleId, field.FieldName);
            }

            var valueToStamp = sample.CorruptFieldNames?.Contains(field.FieldName) == true
                ? Corrupt(Pad(field.FieldName, expected, field.CharacterCount))
                : Pad(field.FieldName, expected, field.CharacterCount);

            StampField(ink, field, valueToStamp);
        }

        return DocumentRaster.Create(layout.Width, layout.Height, ink);
    }

    private static void StampField(bool[,] ink, OcrTemplateField field, string paddedValue)
    {
        for (var i = 0; i < field.CharacterCount; i++)
        {
            var character = paddedValue[i];
            if (!OcrGlyphFont.Glyphs.TryGetValue(character, out var glyph))
            {
                throw DocumentTemplateDraftError.UnsupportedCharacter(field.FieldName, character);
            }

            var cellLeft = field.Left + i * OcrGlyphFont.CellPitchX;
            for (var y = 0; y < OcrGlyphFont.GlyphHeight; y++)
            {
                for (var x = 0; x < OcrGlyphFont.GlyphWidth; x++)
                {
                    if (glyph[y, x])
                    {
                        ink[field.Top + y, cellLeft + x] = true;
                    }
                }
            }
        }
    }

    private static string Pad(string fieldName, string value, int characterCount)
    {
        if (value.Length > characterCount)
        {
            throw DocumentTemplateDraftError.ValueTooLongForField(fieldName, characterCount);
        }

        return value.PadRight(characterCount, OcrGlyphFont.Space);
    }

    /// <summary>Deterministically produces a DIFFERENT fixed-width value than <paramref name="padded"/> in every case, including an all-repeated-character value (e.g. "AAAA", where a reverse/rotate would be a no-op) - flips index 0 to a character guaranteed unequal to its original.</summary>
    private static string Corrupt(string padded)
    {
        if (padded.Length == 0)
        {
            return padded;
        }

        var characters = padded.ToCharArray();
        characters[0] = characters[0] == 'A' ? 'B' : 'A';
        return new string(characters);
    }
}
