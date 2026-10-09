using Biolog.Features.DocumentVerify.Model;

namespace Biolog.Features.MrzEngine.Model;

/// <summary>
/// Turns a detected MRZ region's recognized glyphs into structured fields with independently
/// recalculated check digits (BIO-DOC-002-AC02/AC03/AC04/AC05). This is the one place format
/// ambiguity, low recognition confidence and check-digit mismatches are turned into
/// <see cref="MrzReadOutcome.RequiresManualReview"/> - by reporting what was actually read and
/// computed, never by substituting a value that would make a check pass.
/// </summary>
public static class MrzParser
{
    /// <summary>A field or check digit read below this confidence is flagged for review rather
    /// than trusted outright (AC05). Chosen well above the ~50% confidence two visually similar
    /// glyphs (e.g. '0'/'O', "OptionalData"'s filler '&lt;' vs '0') would produce on a genuine
    /// misread, and well below the near-1.0 a clean synthetic render always produces.</summary>
    public const double MinAcceptableConfidence = 0.85;

    public static MrzReadOutcome Parse(DocumentRaster raster, MrzDetectionResult detection)
    {
        if (!detection.Found)
        {
            return MrzReadOutcome.NotDetected(detection.RejectionReason!);
        }

        var region = ComputeRegionBoundingBox(raster, detection.Lines);
        var recognizedLines = detection.Lines.Select(line => MrzGlyphRecognizer.RecognizeLine(raster, line)).ToList();
        var rawLines = recognizedLines.Select(cells => new string(cells.Select(c => c.Character).ToArray())).ToList();

        var candidates = MrzFormatRegistry.ResolveByShape(detection.Lines.Count, detection.Lines[0].LineLength);
        var format = candidates.Count == 1 ? candidates[0] : MrzFormatRegistry.ResolveByDocumentCode(candidates, rawLines[0][0]);

        if (format is null)
        {
            return new MrzReadOutcome(
                detected: true,
                format: null,
                region: region,
                fields: [],
                compositeCheckDigitApplicable: false,
                compositeCheckDigitValid: null,
                requiresManualReview: true,
                reviewReasons: ["ambiguous_document_format"]);
        }

        var fieldNames = BuildFieldNames(format.Fields);
        var reviewReasons = new List<string>();
        var fields = new List<MrzFieldRead>(format.Fields.Count);

        for (var i = 0; i < format.Fields.Count; i++)
        {
            var spec = format.Fields[i];
            var fieldName = fieldNames[i];
            var rawValue = rawLines[spec.Line].Substring(spec.Start, spec.Length);
            var normalizedValue = Normalize(spec.Kind, rawValue);
            var cellsInField = recognizedLines[spec.Line].Skip(spec.Start).Take(spec.Length).ToList();
            var confidence = cellsInField.Min(c => c.Confidence);
            var bbox = ComputeFieldBoundingBox(raster, cellsInField);

            char? readCheckDigit = null;
            int? computedCheckDigit = null;
            bool? checkDigitValid = null;

            if (spec.HasCheckDigit)
            {
                readCheckDigit = rawLines[spec.Line][spec.CheckDigitColumn];
                computedCheckDigit = MrzCheckDigit.IsComputable(rawValue) ? MrzCheckDigit.Compute(rawValue) : null;
                checkDigitValid = computedCheckDigit.HasValue && readCheckDigit is >= '0' and <= '9'
                    ? computedCheckDigit.Value == readCheckDigit.Value - '0'
                    : false;

                if (checkDigitValid != true)
                {
                    reviewReasons.Add($"check_digit_mismatch:{fieldName}");
                }
            }

            if (confidence < MinAcceptableConfidence)
            {
                reviewReasons.Add($"low_confidence:{fieldName}");
            }

            fields.Add(new MrzFieldRead(
                spec.Kind, fieldName, rawValue, normalizedValue, confidence, bbox,
                spec.HasCheckDigit, readCheckDigit, computedCheckDigit, checkDigitValid));
        }

        bool? compositeValid = null;
        if (format.HasCompositeCheckDigit)
        {
            var compositeInput = BuildCompositeInput(format.Kind, rawLines);
            var compositeColumn = CompositeCheckDigitColumn(format.Kind);
            var readComposite = rawLines[compositeColumn.Line][compositeColumn.Column];
            var computedComposite = MrzCheckDigit.IsComputable(compositeInput) ? MrzCheckDigit.Compute(compositeInput) : (int?)null;
            compositeValid = computedComposite.HasValue && readComposite is >= '0' and <= '9'
                ? computedComposite.Value == readComposite - '0'
                : false;

            if (compositeValid != true)
            {
                reviewReasons.Add("composite_check_digit_mismatch");
            }
        }

        return new MrzReadOutcome(
            detected: true,
            format: format.Kind,
            region: region,
            fields: fields,
            compositeCheckDigitApplicable: format.HasCompositeCheckDigit,
            compositeCheckDigitValid: compositeValid,
            requiresManualReview: reviewReasons.Count > 0,
            reviewReasons: reviewReasons);
    }

    private static BoundingBox ComputeRegionBoundingBox(DocumentRaster raster, IReadOnlyList<MrzDetectedLine> lines)
    {
        var top = lines[0].Top;
        var bottom = lines[^1].Top + MrzGlyphFont.GlyphHeight;
        var left = lines[0].CellStartColumns[0];
        var right = lines.Max(l => l.CellStartColumns[^1] + MrzGlyphFont.GlyphWidth);

        return BoundingBox.Create(
            (double)left / raster.Width,
            (double)top / raster.Height,
            (double)(right - left) / raster.Width,
            (double)(bottom - top) / raster.Height);
    }

    private static BoundingBox ComputeFieldBoundingBox(DocumentRaster raster, IReadOnlyList<MrzRecognizedCell> cellsInField)
    {
        var left = cellsInField[0].Left;
        var right = cellsInField[^1].Left + MrzGlyphFont.GlyphWidth;
        var top = cellsInField[0].Top;
        var bottom = top + MrzGlyphFont.GlyphHeight;

        return BoundingBox.Create(
            (double)left / raster.Width,
            (double)top / raster.Height,
            (double)(right - left) / raster.Width,
            (double)(bottom - top) / raster.Height);
    }

    private static IReadOnlyList<string> BuildFieldNames(IReadOnlyList<MrzFieldSpec> fields)
    {
        var counts = fields.GroupBy(f => f.Kind).ToDictionary(g => g.Key, g => g.Count());
        var seen = new Dictionary<MrzFieldKind, int>();
        var names = new List<string>(fields.Count);

        foreach (var field in fields)
        {
            var baseName = BaseName(field.Kind);
            if (counts[field.Kind] == 1)
            {
                names.Add(baseName);
            }
            else
            {
                seen[field.Kind] = seen.GetValueOrDefault(field.Kind) + 1;
                names.Add($"{baseName}_{seen[field.Kind]}");
            }
        }

        return names;
    }

    private static string BaseName(MrzFieldKind kind) => kind switch
    {
        MrzFieldKind.DocumentCode => "document_code",
        MrzFieldKind.IssuingState => "issuing_state",
        MrzFieldKind.Name => "name",
        MrzFieldKind.DocumentNumber => "document_number",
        MrzFieldKind.Nationality => "nationality",
        MrzFieldKind.DateOfBirth => "date_of_birth",
        MrzFieldKind.Sex => "sex",
        MrzFieldKind.Expiry => "expiry",
        MrzFieldKind.PersonalNumber => "personal_number",
        MrzFieldKind.OptionalData => "optional_data",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    private static string Normalize(MrzFieldKind kind, string raw) => kind switch
    {
        MrzFieldKind.Name => NormalizeName(raw),
        MrzFieldKind.Sex => raw switch { "M" => "male", "F" => "female", _ => "unspecified" },
        MrzFieldKind.DateOfBirth or MrzFieldKind.Expiry => NormalizeDate(raw),
        _ => raw.TrimEnd('<').Replace('<', ' '),
    };

    private static string NormalizeName(string raw)
    {
        var parts = raw.Split("<<", 2);
        var surname = parts[0].Replace('<', ' ').Trim();
        var given = parts.Length > 1 ? parts[1].TrimEnd('<').Replace('<', ' ').Trim() : string.Empty;
        return given.Length > 0 ? $"{surname}, {given}" : surname;
    }

    private static string NormalizeDate(string raw) =>
        raw.Length == 6 ? $"{raw[..2]}-{raw[2..4]}-{raw[4..6]}" : raw;

    private static string BuildCompositeInput(MrzFormatKind kind, IReadOnlyList<string> lines) => kind switch
    {
        MrzFormatKind.Td3 => lines[1][..10] + lines[1][13..20] + lines[1][21..28] + lines[1][28..43],
        MrzFormatKind.Td2 => lines[1][..10] + lines[1][13..20] + lines[1][21..28] + lines[1][28..35],
        MrzFormatKind.Td1 => lines[0][5..30] + lines[1][..7] + lines[1][8..15],
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "format has no composite check digit"),
    };

    private static (int Line, int Column) CompositeCheckDigitColumn(MrzFormatKind kind) => kind switch
    {
        MrzFormatKind.Td3 => (1, 43),
        MrzFormatKind.Td2 => (1, 35),
        MrzFormatKind.Td1 => (1, 29),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "format has no composite check digit"),
    };
}
