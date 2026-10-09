using Biolog.Features.MrzEngine.Model;

namespace Biolog.Features.DocumentClassifier.Model;

/// <summary>
/// Composes MRZ/barcode/OCR evidence into a ranked document classification (BIO-DOC-006-AC01
/// through AC05). Named <c>Classifier</c>, not <c>DocumentClassifier</c>, to avoid the exact
/// namespace/class-name collision (CS0234) BIO-DOC-002's own facade class hit and fixed by
/// renaming <c>MrzEngine</c> to <c>MrzReader</c>: this project's namespace root is already
/// <c>Biolog.Features.DocumentClassifier</c>, so a class of that same name here would make an
/// unqualified reference from any file outside this namespace ambiguous between the namespace
/// segment and the type.
///
/// <para>
/// <b>Why an OCR template match is the primary signal, and MRZ/barcode only corroborate:</b>
/// AC01 asks the classifier to identify country, document class AND series/version together.
/// ICAO Doc 9303 MRZ carries an issuing-state code and a coarse document-code letter, but no
/// series/version field - two different print runs of the same passport type read an identical
/// MRZ. A raw barcode payload is opaque bytes at this layer (interpreting it is BIO-DOC-005's
/// job, and BIO-DOC-005 is deliberately not a dependency of this story). Only a registered
/// <c>OcrDocumentTemplate</c> match can genuinely name a specific series, because the template
/// itself is what a human operator registered as "this is what series X's layout looks like" -
/// so this classifier only ever produces a candidate from a template match, and uses the MRZ
/// issuing-state and barcode-symbology evidence purely to corroborate (raise confidence in) a
/// template match that has already happened, never to invent a candidate on its own. This is a
/// narrower reading of "classifier" than the AC's wording might first suggest, and is documented
/// here explicitly for the same reason BIO-DOC-004's AC03 scope note was: the AC's own wording
/// (all three of country/class/series together) tells you the boundary once traced back to what
/// each engine can actually prove.
/// </para>
/// </summary>
public static class Classifier
{
    /// <summary>A candidate below this confidence makes the whole outcome <c>Unknown</c> (AC02).</summary>
    public const double MinAcceptableConfidence = 0.70;

    /// <summary>Top two candidates scoring within this margin of each other are ambiguous - never
    /// silently picked between (AC05), the same discipline as BIO-DOC-002's
    /// <c>MrzFormatRegistry.ResolveByDocumentCode</c> and BIO-DOC-004's grid-hypothesis line
    /// detector.</summary>
    public const double AmbiguityMargin = 0.05;

    private const double MrzCorroborationBoost = 0.05;
    private const double BarcodeCorroborationBoost = 0.05;

    public static DocumentClassificationOutcome Classify(DocumentClassificationSignals signals, DocumentIntelligenceRegistry registry)
    {
        var candidates = new List<DocumentClassCandidate>();

        foreach (var (templateId, ocrResult) in signals.TemplateMatches)
        {
            var entry = registry.GetByTemplateId(templateId);
            if (entry is null)
            {
                // A template match against a template this registry does not know is not a
                // catalog identity - it cannot contribute a (country, class, series) candidate.
                continue;
            }

            var reasons = new List<string> { $"ocr_template_match:{templateId}" };
            var confidence = ocrResult.Fields.Count > 0 ? ocrResult.Fields.Average(f => f.Confidence) : 0.0;

            var issuingState = signals.Mrz is { Detected: true } mrz
                ? mrz.Fields.FirstOrDefault(f => f.Kind == MrzFieldKind.IssuingState)?.NormalizedValue
                : null;
            if (issuingState is not null && string.Equals(issuingState.Trim(), entry.Country, StringComparison.OrdinalIgnoreCase))
            {
                confidence = Math.Min(1.0, confidence + MrzCorroborationBoost);
                reasons.Add("mrz_issuing_state_matches");
            }

            if (entry.ExpectedSymbologies.Count > 0 && signals.Barcodes.Any(b => entry.ExpectedSymbologies.Contains(b.Symbology)))
            {
                confidence = Math.Min(1.0, confidence + BarcodeCorroborationBoost);
                reasons.Add("barcode_symbology_matches");
            }

            candidates.Add(new DocumentClassCandidate(entry.Country, entry.DocumentClass, entry.Series, confidence, reasons, entry));
        }

        candidates = [.. candidates
            .OrderByDescending(c => c.Confidence)
            .ThenBy(c => c.RegistryEntry.EntryId, StringComparer.Ordinal)];

        var best = candidates.Count > 0 ? candidates[0] : null;
        var unknown = best is null || best.Confidence < MinAcceptableConfidence;

        var confirmationReasons = new List<string>();
        if (unknown)
        {
            confirmationReasons.Add(candidates.Count == 0 ? "no_candidate_matched" : "confidence_below_threshold");
        }

        var detectedSide = DocumentSide.Unknown;
        var sideMismatch = false;
        if (!unknown && best is not null)
        {
            detectedSide = best.RegistryEntry.Side;
            if (signals.ExpectedSide is { } expected && expected != detectedSide)
            {
                sideMismatch = true;
                confirmationReasons.Add($"side_mismatch:detected={detectedSide},expected={expected}");
            }

            if (candidates.Count > 1
                && candidates[1].Confidence >= MinAcceptableConfidence
                && best.Confidence - candidates[1].Confidence < AmbiguityMargin)
            {
                confirmationReasons.Add("ambiguous_top_candidates");
            }

            if (signals.TemplateMatches.TryGetValue(best.RegistryEntry.TemplateId, out var winningResult) && winningResult.RequiresManualReview)
            {
                confirmationReasons.Add($"template_match_requires_review:{best.RegistryEntry.TemplateId}");
            }
        }

        var selectedTemplateId = unknown ? null : best!.RegistryEntry.TemplateId;

        return new DocumentClassificationOutcome(
            candidates,
            unknown,
            detectedSide,
            signals.ExpectedSide,
            sideMismatch,
            requiresOperatorConfirmation: confirmationReasons.Count > 0,
            confirmationReasons,
            selectedTemplateId);
    }
}
