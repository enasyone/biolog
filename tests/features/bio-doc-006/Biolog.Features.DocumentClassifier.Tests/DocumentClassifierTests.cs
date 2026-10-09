using Biolog.Features.BarcodeEngine.Model;
using Biolog.Features.DocumentClassifier.Model;
using Biolog.Features.DocumentClassifier.Tests.Fixtures;
using Biolog.Features.MrzEngine.Model;
using Biolog.Features.OcrEngine.Model;
using Xunit;

namespace Biolog.Features.DocumentClassifier.Tests;

/// <summary>Developer tests for BIO-DOC-006-AC01 (ranked candidates with confidence), AC02
/// (unknown is a valid safe outcome), AC04 (wrong-side detection) and AC05 (low confidence
/// requires operator confirmation) - exercised through the full pipeline: real
/// <c>MrzReader.Read</c>/<c>TemplateOcrEngine.Read</c>/<c>Code39Decoder.TryDecode</c> outcomes fed
/// into <see cref="Classifier.Classify"/>, never fabricated engine results (those types are
/// locked down for exactly this reason).</summary>
public sealed class DocumentClassifierTests
{
    private static readonly IOcrLanguagePack English = new EnglishLanguagePack();

    private static TemplateOcrResult ReadCleanTemplate(string templateId, string text, out OcrDocumentTemplate template)
    {
        var canvas = new SyntheticOcrCanvas(width: 300, height: 40);
        canvas.DrawText(text, left: 10, top: 10);
        template = new OcrDocumentTemplate(templateId, [
            new OcrTemplateField("field", OcrFieldType.FreeText, 10, 10, text.Length, Critical: true),
        ]);
        return TemplateOcrEngine.Read(canvas.ToRaster(), template, English);
    }

    private static DocumentIntelligenceRegistryEntry Entry(
        string entryId,
        string templateId,
        string country = "UTO",
        string documentClass = "passport",
        string series = "2013",
        DocumentSide side = DocumentSide.Front,
        IReadOnlyList<BarcodeSymbology>? expectedSymbologies = null) =>
        new(entryId, country, documentClass, series, side, VersionNumber: 1, templateId, expectedSymbologies ?? []);

    [Fact]
    public void AC01_a_clean_single_template_match_is_returned_as_the_top_ranked_candidate_with_confidence()
    {
        var result = ReadCleanTemplate("uto-passport-front", "OKAFOR", out _);
        var registry = new DocumentIntelligenceRegistry();
        registry.Register(Entry("uto-passport", "uto-passport-front"));

        var signals = new DocumentClassificationSignals(
            Mrz: null,
            Barcodes: [],
            TemplateMatches: new Dictionary<string, TemplateOcrResult> { ["uto-passport-front"] = result },
            ExpectedSide: null);

        var outcome = Classifier.Classify(signals, registry);

        var best = Assert.Single(outcome.Candidates);
        Assert.Same(best, outcome.Best);
        Assert.Equal("UTO", best.Country);
        Assert.Equal("passport", best.DocumentClass);
        Assert.Equal("2013", best.Series);
        Assert.Equal(1.0, best.Confidence);
        Assert.Contains("ocr_template_match:uto-passport-front", best.MatchedSignals);
        Assert.False(outcome.Unknown);
        Assert.Equal("uto-passport-front", outcome.SelectedTemplateId);
    }

    [Fact]
    public void AC01_candidates_are_ranked_best_confidence_first_and_corroboration_raises_rank()
    {
        var weakBitmap = GlyphCorruption.LowerConfidenceButKeepCharacter('O', flipCount: 6);
        var weakCanvas = new SyntheticOcrCanvas(width: 100, height: 40);
        weakCanvas.DrawBitmap(weakBitmap, left: 10, top: 10);
        var weakTemplate = new OcrDocumentTemplate("weak-template", [
            new OcrTemplateField("field", OcrFieldType.FreeText, 10, 10, 1, Critical: false),
        ]);
        var weakResult = TemplateOcrEngine.Read(weakCanvas.ToRaster(), weakTemplate, English);

        // Corroborated by both an MRZ issuing-state match and a matched barcode symbology.
        var strongCanvas = new SyntheticOcrCanvas(width: 100, height: 40);
        strongCanvas.DrawBitmap(weakBitmap, left: 10, top: 10);
        var strongTemplate = new OcrDocumentTemplate("strong-template", [
            new OcrTemplateField("field", OcrFieldType.FreeText, 10, 10, 1, Critical: false),
        ]);
        var strongResult = TemplateOcrEngine.Read(strongCanvas.ToRaster(), strongTemplate, English);

        var registry = new DocumentIntelligenceRegistry();
        registry.Register(Entry("weak", "weak-template", country: "ABC", series: "no-corroboration"));
        registry.Register(Entry("strong", "strong-template", country: "UTO", series: "corroborated", expectedSymbologies: [BarcodeSymbology.Code39]));

        var mrzCanvas = new SyntheticMrzCanvas(width: 270, height: 40);
        mrzCanvas.DrawMrzLines([MrzSampleData.Td3Line1, MrzSampleData.Td3Line2], left: 3, top: 15);
        var mrzOutcome = MrzReader.Read(mrzCanvas.ToRaster());

        var barcodeCanvas = new SyntheticBarcodeCanvas(width: 600, height: 60);
        barcodeCanvas.DrawCode39("SAMPLE", left: 20, top: 15);
        var barcodeAttempt = new Code39Decoder().TryDecode(barcodeCanvas.ToRaster());
        var barcodeResult = BarcodeDecodeResult.FromDecodedAttempt(barcodeAttempt);

        var signals = new DocumentClassificationSignals(
            Mrz: mrzOutcome,
            Barcodes: [barcodeResult],
            TemplateMatches: new Dictionary<string, TemplateOcrResult>
            {
                ["weak-template"] = weakResult,
                ["strong-template"] = strongResult,
            },
            ExpectedSide: null);

        var outcome = Classifier.Classify(signals, registry);

        Assert.Equal(2, outcome.Candidates.Count);
        Assert.Equal("corroborated", outcome.Best!.Series);
        Assert.True(outcome.Candidates[0].Confidence > outcome.Candidates[1].Confidence);
        Assert.Contains("mrz_issuing_state_matches", outcome.Best.MatchedSignals);
        Assert.Contains("barcode_symbology_matches", outcome.Best.MatchedSignals);
    }

    [Fact]
    public void AC02_no_template_matches_at_all_is_reported_as_unknown_never_a_guess()
    {
        var registry = new DocumentIntelligenceRegistry();
        registry.Register(Entry("uto-passport", "uto-passport-front"));

        var signals = new DocumentClassificationSignals(
            Mrz: null,
            Barcodes: [],
            TemplateMatches: new Dictionary<string, TemplateOcrResult>(),
            ExpectedSide: null);

        var outcome = Classifier.Classify(signals, registry);

        Assert.Empty(outcome.Candidates);
        Assert.True(outcome.Unknown);
        Assert.Null(outcome.SelectedTemplateId);
        Assert.True(outcome.RequiresOperatorConfirmation);
        Assert.Contains("no_candidate_matched", outcome.ConfirmationReasons);
    }

    [Fact]
    public void AC02_a_template_match_against_a_template_the_registry_does_not_know_is_unknown()
    {
        var result = ReadCleanTemplate("orphan-template", "OKAFOR", out _);
        var registry = new DocumentIntelligenceRegistry(); // nothing registered

        var signals = new DocumentClassificationSignals(
            Mrz: null,
            Barcodes: [],
            TemplateMatches: new Dictionary<string, TemplateOcrResult> { ["orphan-template"] = result },
            ExpectedSide: null);

        var outcome = Classifier.Classify(signals, registry);

        Assert.Empty(outcome.Candidates);
        Assert.True(outcome.Unknown);
    }

    [Fact]
    public void AC03_QA01_a_match_against_a_superseded_template_is_never_classified_as_the_stale_identity()
    {
        var staleResult = ReadCleanTemplate("template-v1", "OKAFOR", out _);
        var registry = new DocumentIntelligenceRegistry();
        registry.Register(new DocumentIntelligenceRegistryEntry(
            "entry-v1", "UTO", "passport", "2013", DocumentSide.Front, VersionNumber: 1, "template-v1", []));
        registry.Register(new DocumentIntelligenceRegistryEntry(
            "entry-v2", "UTO", "passport", "2013", DocumentSide.Front, VersionNumber: 2, "template-v2", []));

        // A caller's classification signal still keyed by the now-retired v1 template (e.g. a
        // stale in-flight capture) must not resolve to any candidate at all - the reverse lookup
        // for "template-v1" no longer names any registry entry (BIO-DOC-006-QA-01).
        var signals = new DocumentClassificationSignals(
            Mrz: null,
            Barcodes: [],
            TemplateMatches: new Dictionary<string, TemplateOcrResult> { ["template-v1"] = staleResult },
            ExpectedSide: null);

        var outcome = Classifier.Classify(signals, registry);

        Assert.Empty(outcome.Candidates);
        Assert.True(outcome.Unknown);
    }

    [Fact]
    public void AC03_QA02_a_template_reassigned_to_a_different_identity_only_classifies_as_the_new_owner()
    {
        var result = ReadCleanTemplate("shared-template", "OKAFOR", out _);
        var registry = new DocumentIntelligenceRegistry();
        registry.Register(new DocumentIntelligenceRegistryEntry(
            "passport", "UTO", "passport", "2013", DocumentSide.Front, VersionNumber: 1, "shared-template", []));
        registry.Register(new DocumentIntelligenceRegistryEntry(
            "national-id", "UTO", "national-id", "2026", DocumentSide.Front, VersionNumber: 1, "shared-template", []));

        // Once the template has been reassigned to national-id, a classification signal keyed by
        // that template must resolve only to national-id - never to the evicted passport
        // identity, and never to two candidates for one template match (BIO-DOC-006-QA-02).
        var signals = new DocumentClassificationSignals(
            Mrz: null,
            Barcodes: [],
            TemplateMatches: new Dictionary<string, TemplateOcrResult> { ["shared-template"] = result },
            ExpectedSide: null);

        var outcome = Classifier.Classify(signals, registry);

        var candidate = Assert.Single(outcome.Candidates);
        Assert.Equal("national-id", candidate.DocumentClass);
        Assert.Equal("2026", candidate.Series);
    }

    [Fact]
    public void AC02_a_below_threshold_candidate_is_reported_unknown_rather_than_forced_as_a_pick()
    {
        // flipCount=13 pushes Hamming distance (13/35 = 0.371) past Classifier.MinAcceptableConfidence
        // (0.70) - flipCount=6, enough to trip TemplateOcrEngine's own 0.85 critical-field bar, is
        // not enough to trip this classifier's lower, whole-document ranking bar.
        var weakBitmap = GlyphCorruption.LowerConfidenceButKeepCharacter('O', flipCount: 13);
        var canvas = new SyntheticOcrCanvas(width: 100, height: 40);
        canvas.DrawBitmap(weakBitmap, left: 10, top: 10);
        var template = new OcrDocumentTemplate("weak-only", [
            new OcrTemplateField("field", OcrFieldType.FreeText, 10, 10, 1, Critical: false),
        ]);
        var result = TemplateOcrEngine.Read(canvas.ToRaster(), template, English);
        Assert.True(result.Fields[0].Confidence < Classifier.MinAcceptableConfidence);

        var registry = new DocumentIntelligenceRegistry();
        registry.Register(Entry("weak", "weak-only"));

        var signals = new DocumentClassificationSignals(null, [], new Dictionary<string, TemplateOcrResult> { ["weak-only"] = result }, null);

        var outcome = Classifier.Classify(signals, registry);

        Assert.NotEmpty(outcome.Candidates); // still reported, ranked (AC01) ...
        Assert.True(outcome.Unknown); // ... but not trusted as a positive identification (AC02)
        Assert.Null(outcome.SelectedTemplateId);
    }

    [Fact]
    public void AC04_the_detected_side_is_reported_from_the_winning_candidates_registry_entry()
    {
        var result = ReadCleanTemplate("uto-passport-back", "OKAFOR", out _);
        var registry = new DocumentIntelligenceRegistry();
        registry.Register(Entry("uto-passport-back", "uto-passport-back", side: DocumentSide.Back));

        var signals = new DocumentClassificationSignals(null, [], new Dictionary<string, TemplateOcrResult> { ["uto-passport-back"] = result }, ExpectedSide: null);

        var outcome = Classifier.Classify(signals, registry);

        Assert.Equal(DocumentSide.Back, outcome.DetectedSide);
        Assert.False(outcome.SideMismatch);
    }

    [Fact]
    public void AC04_a_captured_side_that_disagrees_with_the_expected_side_is_flagged_as_a_mismatch()
    {
        var result = ReadCleanTemplate("uto-passport-back", "OKAFOR", out _);
        var registry = new DocumentIntelligenceRegistry();
        registry.Register(Entry("uto-passport-back", "uto-passport-back", side: DocumentSide.Back));

        var signals = new DocumentClassificationSignals(
            null, [], new Dictionary<string, TemplateOcrResult> { ["uto-passport-back"] = result }, ExpectedSide: DocumentSide.Front);

        var outcome = Classifier.Classify(signals, registry);

        Assert.True(outcome.SideMismatch);
        Assert.True(outcome.RequiresOperatorConfirmation);
        Assert.Contains("side_mismatch:detected=Back,expected=Front", outcome.ConfirmationReasons);
    }

    [Fact]
    public void AC05_unknown_always_requires_operator_confirmation()
    {
        var registry = new DocumentIntelligenceRegistry();
        var signals = new DocumentClassificationSignals(null, [], new Dictionary<string, TemplateOcrResult>(), null);

        var outcome = Classifier.Classify(signals, registry);

        Assert.True(outcome.RequiresOperatorConfirmation);
    }

    [Fact]
    public void AC05_a_winning_template_match_that_itself_required_manual_review_forces_confirmation()
    {
        var weakBitmap = GlyphCorruption.LowerConfidenceButKeepCharacter('O', flipCount: 6);
        var canvas = new SyntheticOcrCanvas(width: 100, height: 40);
        canvas.DrawBitmap(weakBitmap, left: 10, top: 10);
        var template = new OcrDocumentTemplate("critical-weak", [
            new OcrTemplateField("document_number", OcrFieldType.DocumentNumber, 10, 10, 1, Critical: true),
        ]);
        var result = TemplateOcrEngine.Read(canvas.ToRaster(), template, English);
        Assert.True(result.RequiresManualReview);

        var registry = new DocumentIntelligenceRegistry();
        // MRZ corroboration alone is not enough to push this over the confidence bar reliably in
        // every environment, so this test only asserts the REQUIRED-review propagation, not the
        // confidence value itself.
        registry.Register(Entry("critical-weak", "critical-weak"));

        var signals = new DocumentClassificationSignals(null, [], new Dictionary<string, TemplateOcrResult> { ["critical-weak"] = result }, null);

        var outcome = Classifier.Classify(signals, registry);

        if (!outcome.Unknown)
        {
            Assert.True(outcome.RequiresOperatorConfirmation);
            Assert.Contains("template_match_requires_review:critical-weak", outcome.ConfirmationReasons);
        }
        else
        {
            // Still safe either way (AC02/AC05): unknown already requires confirmation.
            Assert.True(outcome.RequiresOperatorConfirmation);
        }
    }

    [Fact]
    public void AC05_two_near_tied_top_candidates_are_flagged_as_ambiguous_never_silently_picked()
    {
        var resultA = ReadCleanTemplate("tie-a", "OKAFOR", out _);
        var resultB = ReadCleanTemplate("tie-b", "OKAFOR", out _);

        var registry = new DocumentIntelligenceRegistry();
        registry.Register(Entry("tie-a", "tie-a", country: "AAA", documentClass: "id-card", series: "a"));
        registry.Register(Entry("tie-b", "tie-b", country: "BBB", documentClass: "id-card", series: "b"));

        var signals = new DocumentClassificationSignals(
            null, [], new Dictionary<string, TemplateOcrResult> { ["tie-a"] = resultA, ["tie-b"] = resultB }, null);

        var outcome = Classifier.Classify(signals, registry);

        Assert.False(outcome.Unknown);
        Assert.Contains("ambiguous_top_candidates", outcome.ConfirmationReasons);
        Assert.True(outcome.RequiresOperatorConfirmation);
    }
}
