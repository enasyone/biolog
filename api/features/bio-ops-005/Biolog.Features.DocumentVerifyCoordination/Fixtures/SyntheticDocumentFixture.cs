using Biolog.Features.BarcodeEngine.Model;
using MrzModel = Biolog.Features.MrzEngine.Model;
using OcrModel = Biolog.Features.OcrEngine.Model;

namespace Biolog.Features.DocumentVerifyCoordination.Fixtures;

/// <summary>
/// Kemi's BIO-OPS-005 ruling, point 6 (the raster boundary, "disclosed, not solved here"): no
/// image-decoding library exists anywhere in this repo - confirmed, and explicitly documented as
/// intentional by BIO-DOC-002's own <c>DocumentRaster.cs</c> ("turning real container-format
/// bytes into a pixel grid is a scanner/camera-integration concern for a later story"). This type
/// builds ONE deterministic synthetic document - a Nigerian ePassport, matching
/// <c>NigeriaDocumentPackV1.PassportEntry</c>'s own field vocabulary - and renders it into each
/// engine's own native raster type, using the SAME technique every BIO-DOC-00x engine's own test
/// suite already uses to prove itself (stamp each engine's own public glyph-font/symbology tables
/// directly, at the pitch/position a real captured image of this document would carry). Every
/// downstream engine call (<c>MrzReader.Read</c>, <c>BarcodeDetectionService.Scan</c>,
/// <c>TemplateOcrEngine.Read</c>) then runs its genuine recognition/validation code against that
/// raster and produces genuinely computed evidence - only the INPUT image is synthetic, the exact
/// same boundary BIO-DOC-002 already discloses, never the engine's own behaviour.
///
/// <para>
/// (MRZ's own <c>DocumentRaster</c> and OCR's own <c>DocumentRaster</c> are two distinct types,
/// one per engine - see each engine's own type's remarks for why this story does not unify them -
/// aliased to <c>MrzModel</c>/<c>OcrModel</c> below purely to keep that name unambiguous in one
/// file, not a sign the two have been merged.)
/// </para>
///
/// <para>
/// Deterministic per session: every session gets the IDENTICAL fixture (there is exactly one
/// synthetic document this story ships, not a per-session random document) - this mirrors
/// BIO-DOC-009/010's own already-reviewed demo-fixture shape (a small, fixed set of named
/// scenarios cycled through, never a randomly generated one) and keeps every call fully
/// reproducible for tests and audits.
/// </para>
/// </summary>
public static class SyntheticDocumentFixture
{
    // Declared field values, matching NigeriaDocumentPackV1.PassportEntryId's own field
    // vocabulary (api/features/bio-doc-010/.../Data/NigeriaDocumentPackV1.cs) exactly, so
    // IdentifyDocumentAsync's observed-fields map lines up with that pack entry's own required
    // fields/patterns without a separate "is this the same document" guess.
    public const string Surname = "ADEYEMI";
    public const string GivenNames = "CHIOMA FUNMILAYO";
    public const string PassportNumber = "A12345678";
    public const string DateOfBirthIso = "1990-05-14";
    public const string Sex = "F";
    public const string DateOfExpiryIso = "2031-05-13";
    public const string NationalIdentityNumber = "12345678901";

    /// <summary>MRZ's own field name for the field this coordinator cross-compares against
    /// OCR's "PassportNumber" (BIO-DOC-002's own <c>MrzParser</c> names it "document_number" -
    /// see that engine's <c>BaseName</c> switch).</summary>
    public const string MrzDocumentNumberFieldName = "document_number";

    public static MrzModel.DocumentRaster BuildMrzRaster()
    {
        var dobRaw = IsoDateToMrzYyMmDd(DateOfBirthIso);
        var expiryRaw = IsoDateToMrzYyMmDd(DateOfExpiryIso);
        var personalNumber = (NationalIdentityNumber + "<<<<<<<<<<<<<<")[..14];

        var documentNumberCheck = MrzModel.MrzCheckDigit.Compute(PassportNumber);
        var dobCheck = MrzModel.MrzCheckDigit.Compute(dobRaw);
        var expiryCheck = MrzModel.MrzCheckDigit.Compute(expiryRaw);
        var personalNumberCheck = MrzModel.MrzCheckDigit.Compute(personalNumber);

        var line2 =
            PassportNumber + documentNumberCheck +
            "NGA" +
            dobRaw + dobCheck +
            Sex +
            expiryRaw + expiryCheck +
            personalNumber + personalNumberCheck;

        var compositeInput = line2[..10] + line2[13..20] + line2[21..28] + line2[28..43];
        var compositeCheck = MrzModel.MrzCheckDigit.Compute(compositeInput);
        line2 += compositeCheck;

        var rawName = Surname + "<<" + GivenNames.Replace(' ', '<');
        var name = (rawName + new string('<', 39))[..39];
        var line1 = "P<" + "NGA" + name;

        const int left = 3;
        const int top = 15;
        var canvas = new MrzCanvas(width: 270, height: 40);
        canvas.DrawLines([line1, line2], left, top);
        return canvas.ToRaster();
    }

    public static BarcodeRaster BuildBarcodeRaster()
    {
        var canvas = new BarcodeCanvas(width: 600, height: 60);
        canvas.DrawCode39(PassportNumber, left: 20, top: 15);
        return canvas.ToRaster();
    }

    /// <summary>The OCR template this fixture's visual zone is rendered against - field names
    /// deliberately equal <c>NigeriaDocumentPackV1.PassportEntry</c>'s own field names 1:1
    /// (<c>Surname</c>, <c>GivenNames</c>, ...), so <c>IdentifyDocumentAsync</c> needs no
    /// translation table between what OCR reads and what the governed pack expects.</summary>
    public static OcrModel.OcrDocumentTemplate BuildOcrTemplate()
    {
        var fields = new List<OcrModel.OcrTemplateField>
        {
            new("Surname", OcrModel.OcrFieldType.Surname, Left: 10, Top: 10, CharacterCount: Surname.Length, Critical: true),
            new("GivenNames", OcrModel.OcrFieldType.GivenNames, Left: 10, Top: 10 + 1 * OcrModel.OcrGlyphFont.CellPitchY, CharacterCount: GivenNames.Length, Critical: true),
            new("PassportNumber", OcrModel.OcrFieldType.DocumentNumber, Left: 10, Top: 10 + 2 * OcrModel.OcrGlyphFont.CellPitchY, CharacterCount: PassportNumber.Length, Critical: true),
            new("DateOfBirth", OcrModel.OcrFieldType.FreeText, Left: 10, Top: 10 + 3 * OcrModel.OcrGlyphFont.CellPitchY, CharacterCount: DateOfBirthIso.Length, Critical: true),
            new("Sex", OcrModel.OcrFieldType.FreeText, Left: 10, Top: 10 + 4 * OcrModel.OcrGlyphFont.CellPitchY, CharacterCount: Sex.Length, Critical: false),
            new("DateOfExpiry", OcrModel.OcrFieldType.FreeText, Left: 10, Top: 10 + 5 * OcrModel.OcrGlyphFont.CellPitchY, CharacterCount: DateOfExpiryIso.Length, Critical: false),
            new("NationalIdentityNumber", OcrModel.OcrFieldType.FreeText, Left: 10, Top: 10 + 6 * OcrModel.OcrGlyphFont.CellPitchY, CharacterCount: NationalIdentityNumber.Length, Critical: false),
        };

        return new OcrModel.OcrDocumentTemplate("fixture-nga-passport-2021", fields);
    }

    public static OcrModel.DocumentRaster BuildOcrRaster()
    {
        var template = BuildOcrTemplate();
        var width = template.Fields.Max(f => f.Left + f.CharacterCount * OcrModel.OcrGlyphFont.CellPitchX) + 10;
        var height = template.Fields.Max(f => f.Top + OcrModel.OcrGlyphFont.GlyphHeight) + 10;

        var canvas = new OcrCanvas(width, height);
        var valuesByField = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Surname"] = Surname,
            ["GivenNames"] = GivenNames,
            ["PassportNumber"] = PassportNumber,
            ["DateOfBirth"] = DateOfBirthIso,
            ["Sex"] = Sex,
            ["DateOfExpiry"] = DateOfExpiryIso,
            ["NationalIdentityNumber"] = NationalIdentityNumber,
        };

        foreach (var field in template.Fields)
        {
            canvas.DrawText(valuesByField[field.FieldName], field.Left, field.Top);
        }

        return canvas.ToRaster();
    }

    private static string IsoDateToMrzYyMmDd(string isoDate)
    {
        // "1990-05-14" -> "900514" (ICAO's own 2-digit-year field shape - this fixture's engine
        // input, not a general calendar library).
        var year = isoDate[2..4];
        var month = isoDate[5..7];
        var day = isoDate[8..10];
        return year + month + day;
    }

    private sealed class MrzCanvas(int width, int height)
    {
        private readonly bool[,] _ink = new bool[height, width];

        public void DrawLines(IReadOnlyList<string> lines, int left, int top)
        {
            for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
            {
                var line = lines[lineIndex];
                var lineTop = top + lineIndex * MrzModel.MrzGlyphFont.CellPitchY;
                for (var col = 0; col < line.Length; col++)
                {
                    DrawGlyph(line[col], left + col * MrzModel.MrzGlyphFont.CellPitchX, lineTop);
                }
            }
        }

        private void DrawGlyph(char character, int left, int top)
        {
            var template = MrzModel.MrzGlyphFont.Glyphs[character];
            for (var y = 0; y < MrzModel.MrzGlyphFont.GlyphHeight; y++)
            {
                for (var x = 0; x < MrzModel.MrzGlyphFont.GlyphWidth; x++)
                {
                    SetPixel(left + x, top + y, template[y, x]);
                }
            }
        }

        private void SetPixel(int x, int y, bool ink)
        {
            if (x < 0 || x >= width || y < 0 || y >= height)
            {
                return;
            }

            _ink[y, x] = ink;
        }

        public MrzModel.DocumentRaster ToRaster() => MrzModel.DocumentRaster.Create(width, height, _ink);
    }

    private sealed class BarcodeCanvas(int width, int height)
    {
        private readonly bool[,] _ink = new bool[height, width];

        public void DrawCode39(string payload, int left, int top)
        {
            var x = left;
            x = DrawCharacter(Code39Table.StartStop, x, top);
            x += Code39Decoder.ModulePx;

            foreach (var c in payload)
            {
                x = DrawCharacter(c, x, top);
                x += Code39Decoder.ModulePx;
            }

            DrawCharacter(Code39Table.StartStop, x, top);
        }

        private int DrawCharacter(char character, int left, int top)
        {
            var pattern = Code39Table.Patterns[character];
            var x = left;
            for (var i = 0; i < pattern.Length; i++)
            {
                var isBar = i % 2 == 0;
                var widthPx = (pattern[i] ? 2 : 1) * Code39Decoder.ModulePx;
                if (isBar)
                {
                    FillRect(x, top, widthPx, Code39Decoder.BarHeightPx);
                }

                x += widthPx;
            }

            return x;
        }

        private void FillRect(int left, int top, int w, int h)
        {
            for (var y = top; y < top + h; y++)
            {
                for (var x = left; x < left + w; x++)
                {
                    if (x >= 0 && x < width && y >= 0 && y < height)
                    {
                        _ink[y, x] = true;
                    }
                }
            }
        }

        public BarcodeRaster ToRaster() => BarcodeRaster.Create(width, height, _ink);
    }

    private sealed class OcrCanvas(int width, int height)
    {
        private readonly bool[,] _ink = new bool[height, width];

        public void DrawText(string text, int left, int top)
        {
            for (var i = 0; i < text.Length; i++)
            {
                DrawGlyph(text[i], left + i * OcrModel.OcrGlyphFont.CellPitchX, top);
            }
        }

        private void DrawGlyph(char character, int left, int top)
        {
            var template = OcrModel.OcrGlyphFont.Glyphs[character];
            for (var y = 0; y < OcrModel.OcrGlyphFont.GlyphHeight; y++)
            {
                for (var x = 0; x < OcrModel.OcrGlyphFont.GlyphWidth; x++)
                {
                    SetPixel(left + x, top + y, template[y, x]);
                }
            }
        }

        private void SetPixel(int x, int y, bool ink)
        {
            if (x < 0 || x >= width || y < 0 || y >= height)
            {
                return;
            }

            _ink[y, x] = ink;
        }

        public OcrModel.DocumentRaster ToRaster() => OcrModel.DocumentRaster.Create(width, height, _ink);
    }
}
