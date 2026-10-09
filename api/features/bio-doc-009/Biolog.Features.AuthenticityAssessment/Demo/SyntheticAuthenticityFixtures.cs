using Biolog.Features.EvidenceComparison.Audit;
using Biolog.Features.EvidenceComparison.Model;

namespace Biolog.Features.AuthenticityAssessment.Demo;

/// <summary>
/// Three named, deterministic SYNTHETIC document sessions (no real/PII data - synthetic
/// identifiers and values only) the BIO-DOC-009 HTTP endpoint cycles through
/// (<c>AuthenticityAssessmentService</c>), so <c>POST /api/v1/documents/authenticity</c> returns a
/// genuinely computed - not canned - <see cref="Model.AuthenticityAssessmentResult"/> every time,
/// covering all three <see cref="Model.AuthenticityDisposition"/> values without needing a real
/// capture/upload pipeline (none exists yet - BIO-UX-009's own document-verify page never uploads
/// real evidence, by design).
///
/// <para>
/// Classification and passive authentication are deliberately absent from every fixture here
/// (<c>Classification: null</c>, an <see cref="NfcCorroboration.NotAvailable"/> NFC signal) - this
/// project's own declared-dependency boundary (BIO-DOC-006, BIO-DOC-008 only) cannot itself
/// produce a non-empty classification candidate or an "available" NFC signal without a real
/// BIO-DOC-004/BIO-DOC-007 result respectively (see this project's own <c>.csproj</c> comment).
/// <see cref="FullyVerified"/> still genuinely reaches <see cref="Model.AuthenticityDisposition.DocumentAuthentic"/>
/// on cross-source field agreement alone, which is real, reachable evidence within this story's own
/// boundary - not a shortcut.
/// </para>
///
/// <para>
/// <b>No static/shared <see cref="IVersionStore"/> or <see cref="IEvidenceComparisonAuditSink"/>
/// here, deliberately:</b> an earlier version of this file owned a single process-wide static
/// <see cref="IVersionStore"/>, which desynchronised from a caller's own per-session version
/// bookkeeping (<c>AuthenticityAssessmentService</c>'s own <c>_demoEvidenceVersionBySession</c>)
/// the moment more than one independent caller (a second <c>AuthenticityAssessmentService</c>
/// instance - e.g. a fresh instance per test) computed for the same well-known demo session id:
/// the static store remembered a version from an earlier instance while the new instance's own
/// counter restarted at 1, so BIO-DOC-008's own version-sequence check rejected it
/// (<c>AuthenticityAssessmentServiceTests</c> caught this directly). Fixed by taking the store and
/// sink as parameters - the CALLER (one per <c>AuthenticityAssessmentService</c> instance) now owns
/// consistent, isolated state, the same "no hidden shared mutable state" discipline every other
/// locked-down type in this epic already follows.
/// </para>
/// </summary>
public static class SyntheticAuthenticityFixtures
{
    /// <summary>MRZ and barcode agree on the document number; MRZ and OCR agree on date of
    /// birth - two independently-corroborated fields, no conflicts. Reaches
    /// <see cref="Model.AuthenticityDisposition.DocumentAuthentic"/> via
    /// <c>CrossSourceFieldComparison</c> alone (BIO-DOC-009-AC01's "cross-source" check).</summary>
    public static Model.AuthenticityAssessmentInputs FullyVerified(
        string documentSessionId, int callSequence, IEvidenceComparisonAuditSink auditSink, IVersionStore versionStore)
    {
        var fields = new List<FieldComparison>
        {
            ComparisonEngine.Compare("document_number", new Dictionary<EvidenceSourceKind, SourceFieldValue>
            {
                [EvidenceSourceKind.Mrz] = SourceFieldValue.Create(EvidenceSourceKind.Mrz, "A1234567", "A1234567", 0.97),
                [EvidenceSourceKind.Barcode] = SourceFieldValue.Create(EvidenceSourceKind.Barcode, "A1234567", "A1234567", 1.0),
            }),
            ComparisonEngine.Compare("date_of_birth", new Dictionary<EvidenceSourceKind, SourceFieldValue>
            {
                [EvidenceSourceKind.Mrz] = SourceFieldValue.Create(EvidenceSourceKind.Mrz, "900514", "1990-05-14", 0.96),
                [EvidenceSourceKind.Visual] = SourceFieldValue.Create(EvidenceSourceKind.Visual, "14 MAY 1990", "1990-05-14", 0.91),
            }),
        };

        var crossComparison = CrossComparisonResult.Compute(
            documentSessionId, callSequence, "demo-orchestrator", NowRfc3339(), $"demo-{documentSessionId}-{callSequence}",
            fields, NfcCorroboration.NotAvailable, auditSink, versionStore);

        return new Model.AuthenticityAssessmentInputs
        {
            DocumentSessionId = documentSessionId,
            MrzOutcome = Model.AuthenticityCheckOutcome.Passed,
            MrzDetail = "MRZ read and check digits valid.",
            BarcodeOutcome = Model.AuthenticityCheckOutcome.Passed,
            BarcodeDetail = "Barcode decoded and structurally valid.",
            OcrOutcome = Model.AuthenticityCheckOutcome.Passed,
            OcrDetail = "OCR text extracted at high confidence.",
            Classification = null,
            CrossComparison = crossComparison,
        };
    }

    /// <summary>Only the barcode decoded - nothing else ran. The named AC02 scenario: proves,
    /// live over HTTP and not just in a unit test, that a decoded barcode alone can never reach
    /// <see cref="Model.AuthenticityDisposition.DocumentAuthentic"/>.</summary>
    public static Model.AuthenticityAssessmentInputs BarcodeOnly(
        string documentSessionId, int callSequence, IEvidenceComparisonAuditSink auditSink, IVersionStore versionStore)
    {
        var fields = new List<FieldComparison>
        {
            ComparisonEngine.Compare("document_number", new Dictionary<EvidenceSourceKind, SourceFieldValue>
            {
                [EvidenceSourceKind.Barcode] = SourceFieldValue.Create(EvidenceSourceKind.Barcode, "B7654321", "B7654321", 1.0),
            }),
        };

        var crossComparison = CrossComparisonResult.Compute(
            documentSessionId, callSequence, "demo-orchestrator", NowRfc3339(), $"demo-{documentSessionId}-{callSequence}",
            fields, NfcCorroboration.NotAvailable, auditSink, versionStore);

        return new Model.AuthenticityAssessmentInputs
        {
            DocumentSessionId = documentSessionId,
            MrzOutcome = Model.AuthenticityCheckOutcome.NotPerformed,
            MrzDetail = "MRZ read was not attempted.",
            BarcodeOutcome = Model.AuthenticityCheckOutcome.Passed,
            BarcodeDetail = "Barcode decoded and structurally valid.",
            OcrOutcome = Model.AuthenticityCheckOutcome.NotPerformed,
            OcrDetail = "OCR extraction was not attempted.",
            Classification = null,
            CrossComparison = crossComparison,
        };
    }

    /// <summary>MRZ and OCR disagree on date of birth. Reaches
    /// <see cref="Model.AuthenticityDisposition.DocumentNotAuthentic"/>.</summary>
    public static Model.AuthenticityAssessmentInputs ConflictDetected(
        string documentSessionId, int callSequence, IEvidenceComparisonAuditSink auditSink, IVersionStore versionStore)
    {
        var fields = new List<FieldComparison>
        {
            ComparisonEngine.Compare("date_of_birth", new Dictionary<EvidenceSourceKind, SourceFieldValue>
            {
                [EvidenceSourceKind.Mrz] = SourceFieldValue.Create(EvidenceSourceKind.Mrz, "900514", "1990-05-14", 0.96),
                [EvidenceSourceKind.Visual] = SourceFieldValue.Create(EvidenceSourceKind.Visual, "14 MAY 1991", "1991-05-14", 0.88),
            }),
        };

        var crossComparison = CrossComparisonResult.Compute(
            documentSessionId, callSequence, "demo-orchestrator", NowRfc3339(), $"demo-{documentSessionId}-{callSequence}",
            fields, NfcCorroboration.NotAvailable, auditSink, versionStore);

        return new Model.AuthenticityAssessmentInputs
        {
            DocumentSessionId = documentSessionId,
            MrzOutcome = Model.AuthenticityCheckOutcome.Passed,
            MrzDetail = "MRZ read and check digits valid.",
            BarcodeOutcome = Model.AuthenticityCheckOutcome.NotPerformed,
            BarcodeDetail = "Barcode decode was not attempted.",
            OcrOutcome = Model.AuthenticityCheckOutcome.Passed,
            OcrDetail = "OCR text extracted at high confidence.",
            Classification = null,
            CrossComparison = crossComparison,
        };
    }

    private static string NowRfc3339() => DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture);
}
