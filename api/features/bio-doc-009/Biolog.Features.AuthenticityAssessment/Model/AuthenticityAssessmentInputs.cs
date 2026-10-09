using Biolog.Features.DocumentClassifier.Model;
using Biolog.Features.EvidenceComparison.Model;

namespace Biolog.Features.AuthenticityAssessment.Model;

/// <summary>
/// Everything <see cref="AuthenticityPolicyEngine.Evaluate"/> needs, gathered from exactly this
/// story's two declared dependencies (BIO-DOC-006, BIO-DOC-008) plus plain, caller-supplied
/// structural-check signals for MRZ/barcode/OCR.
///
/// <para>
/// <b>Why MRZ/barcode/OCR are plain outcome+detail pairs, not the concrete BIO-DOC-002/003/004
/// result types:</b> this story's declared DEPENDENCIES line is BIO-DOC-006 and BIO-DOC-008 only -
/// taking a <c>ProjectReference</c> on BIO-DOC-002/003/004 directly would be an undeclared
/// dependency (<c>scripts/check-module-project-references.sh</c>-adjacent discipline this epic
/// enforces by convention, see this project's own <c>.csproj</c> comment). BIO-DOC-008 already
/// established this exact precedent for its own OCR input ("plain caller-supplied
/// <c>SourceFieldValue</c>, not a resolved dependency" - see its own <c>.csproj</c> comment) -
/// applied here to all three undeclared structural engines identically. Whoever assembles these
/// inputs (the BIO-DOC-009 HTTP endpoint, or a future full-pipeline orchestrator) already holds the
/// real engine outcomes and translates them into this plain shape; this library itself never needs
/// to know MRZ/barcode/OCR's own result types to apply policy over their outcomes.
/// </para>
///
/// <para>
/// <b>Why passive authentication has no separate field:</b> BIO-DOC-007 (NFC/passive
/// authentication) is not a declared dependency either, and BIO-DOC-008's own
/// <see cref="CrossComparisonResult.NfcCorroboration"/> already folds that exact cryptographic
/// signal in (<c>PassiveAuthenticationPassed: bool?</c>) - reusing it here avoids a second,
/// possibly-inconsistent copy of the same fact.
/// </para>
/// </summary>
public sealed class AuthenticityAssessmentInputs
{
    public required string DocumentSessionId { get; init; }

    public AuthenticityCheckOutcome MrzOutcome { get; init; } = AuthenticityCheckOutcome.NotPerformed;
    public string MrzDetail { get; init; } = "MRZ read was not attempted.";

    /// <summary>AC02's named check - see <see cref="AuthenticityPolicyEngine"/>'s remarks for why
    /// this outcome alone can never promote the overall disposition to
    /// <see cref="AuthenticityDisposition.DocumentAuthentic"/>.</summary>
    public AuthenticityCheckOutcome BarcodeOutcome { get; init; } = AuthenticityCheckOutcome.NotPerformed;
    public string BarcodeDetail { get; init; } = "Barcode decode was not attempted.";

    public AuthenticityCheckOutcome OcrOutcome { get; init; } = AuthenticityCheckOutcome.NotPerformed;
    public string OcrDetail { get; init; } = "OCR extraction was not attempted.";

    /// <summary>Null when classification was not attempted at all (AC03: distinct from
    /// <see cref="DocumentClassificationOutcome.Unknown"/>, which IS an attempted, Failed
    /// classification).</summary>
    public DocumentClassificationOutcome? Classification { get; init; }

    /// <summary>BIO-DOC-008's already-computed, already-audited cross-source comparison - this
    /// story's own policy layer sits on top of it rather than recomputing it.</summary>
    public required CrossComparisonResult CrossComparison { get; init; }
}
