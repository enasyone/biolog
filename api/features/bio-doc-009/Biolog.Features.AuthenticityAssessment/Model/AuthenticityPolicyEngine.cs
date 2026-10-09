using Biolog.Features.DocumentClassifier.Model;
using Biolog.Features.EvidenceComparison.Model;

namespace Biolog.Features.AuthenticityAssessment.Model;

/// <summary>
/// The pure policy function BIO-DOC-009-AC01/AC02/AC03 describe: given a set of individual check
/// inputs, decide each check's own outcome and the overall <see cref="AuthenticityDisposition"/>.
/// Stateless and side-effect-free - <see cref="AuthenticityAssessmentResult.Compute"/> is the
/// (audited, attributed) caller that wraps this.
///
/// <para>
/// <b>AC02, "barcode decode alone can never produce DOCUMENT AUTHENTIC" - enforced structurally,
/// not by convention:</b> <see cref="DetermineDisposition"/> only ever consults the
/// <see cref="AuthenticityCheckId.CrossSourceFieldComparison"/> and
/// <see cref="AuthenticityCheckId.PassiveAuthentication"/> checks to decide
/// <see cref="AuthenticityDisposition.DocumentAuthentic"/> - <see cref="AuthenticityCheckId.BarcodeDecode"/>'s
/// own outcome is never read there at all, by construction, so no future edit to this method could
/// accidentally let it contribute. This is sufficient on its own because
/// <see cref="BuildCrossSourceCheck"/> can only report <see cref="AuthenticityCheckOutcome.Passed"/>
/// when at least one field had two or more independent sources agree
/// (<see cref="FieldMatchStatus.Match"/>) - BIO-DOC-008's own <see cref="FieldMatchStatus.Unknown"/>
/// rule ("fewer than two sources supplied a value... never silently reported as a match") already
/// guarantees a barcode-only input (nothing else to compare against) can never reach
/// <see cref="FieldMatchStatus.Match"/> for any field. A decoded barcode with no corroborating MRZ/
/// OCR/visual value is therefore, unavoidably, <see cref="AuthenticityCheckOutcome.NotPerformed"/>
/// on cross-source comparison - not <see cref="AuthenticityCheckOutcome.Passed"/> - so it can never
/// satisfy the one rule that promotes to <see cref="AuthenticityDisposition.DocumentAuthentic"/>.
/// See <c>AuthenticityPolicyEngineTests.AC02_...</c> for the executable proof.
/// </para>
///
/// <para>
/// <b>AC03, "unsupported checks are marked not performed, not passed":</b> every <c>BuildXxxCheck</c>
/// helper below defaults to <see cref="AuthenticityCheckOutcome.NotPerformed"/> for an absent/
/// unattempted input and never infers <see cref="AuthenticityCheckOutcome.Passed"/> from the mere
/// absence of a failure.
/// </para>
/// </summary>
public static class AuthenticityPolicyEngine
{
    /// <summary>AC04's "ruleset version" - bumped whenever this method's DECISION LOGIC changes
    /// (not for comment-only edits). Recorded on every <see cref="AuthenticityAssessmentResult"/>
    /// so a later audit can tell which policy produced a given disposition.</summary>
    public const string RulesetVersion = "authenticity-policy-v1";

    public static IReadOnlyList<AuthenticityCheckResult> BuildChecks(AuthenticityAssessmentInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(inputs.CrossComparison);

        return
        [
            new AuthenticityCheckResult(AuthenticityCheckId.MrzStructuralValidity, inputs.MrzOutcome, inputs.MrzDetail),
            new AuthenticityCheckResult(AuthenticityCheckId.BarcodeDecode, inputs.BarcodeOutcome, inputs.BarcodeDetail),
            new AuthenticityCheckResult(AuthenticityCheckId.OcrFieldExtraction, inputs.OcrOutcome, inputs.OcrDetail),
            BuildClassificationCheck(inputs.Classification),
            BuildCrossSourceCheck(inputs.CrossComparison),
            BuildPassiveAuthenticationCheck(inputs.CrossComparison.NfcCorroboration),
        ];
    }

    public static AuthenticityDisposition DetermineDisposition(IReadOnlyList<AuthenticityCheckResult> checks)
    {
        ArgumentNullException.ThrowIfNull(checks);

        if (checks.Any(c => c.Outcome == AuthenticityCheckOutcome.Failed))
        {
            return AuthenticityDisposition.DocumentNotAuthentic;
        }

        var crossSource = checks.Single(c => c.CheckId == AuthenticityCheckId.CrossSourceFieldComparison);
        var passiveAuth = checks.Single(c => c.CheckId == AuthenticityCheckId.PassiveAuthentication);

        // AC02: BarcodeDecode's own outcome is deliberately never read in this method - see this
        // type's own remarks for the structural argument that makes that sufficient.
        var strongEvidence = passiveAuth.Outcome == AuthenticityCheckOutcome.Passed
            || crossSource.Outcome == AuthenticityCheckOutcome.Passed;
        if (strongEvidence)
        {
            return AuthenticityDisposition.DocumentAuthentic;
        }

        if (checks.Any(c => c.Outcome == AuthenticityCheckOutcome.Inconclusive))
        {
            return AuthenticityDisposition.Inconclusive;
        }

        // Nothing failed, nothing inconclusive, and neither strong-evidence check passed (e.g.
        // every check NotPerformed, or only weak/unsupported checks reported anything at all).
        // Never defaults to DocumentAuthentic for lack of a Failed check.
        return AuthenticityDisposition.Inconclusive;
    }

    private static AuthenticityCheckResult BuildClassificationCheck(DocumentClassificationOutcome? classification)
    {
        if (classification is null)
        {
            return AuthenticityCheckResult.NotPerformed(
                AuthenticityCheckId.DocumentClassification, "Document classification was not attempted.");
        }

        if (classification.Unknown)
        {
            return new AuthenticityCheckResult(
                AuthenticityCheckId.DocumentClassification,
                AuthenticityCheckOutcome.Failed,
                "No candidate document template cleared the classifier's confidence threshold.");
        }

        if (classification.RequiresOperatorConfirmation)
        {
            var reasons = classification.ConfirmationReasons.Count > 0
                ? string.Join("; ", classification.ConfirmationReasons)
                : "the classifier flagged this result for operator confirmation.";
            return new AuthenticityCheckResult(
                AuthenticityCheckId.DocumentClassification, AuthenticityCheckOutcome.Inconclusive, reasons);
        }

        return new AuthenticityCheckResult(
            AuthenticityCheckId.DocumentClassification,
            AuthenticityCheckOutcome.Passed,
            $"Classified as template '{classification.SelectedTemplateId}'.");
    }

    private static AuthenticityCheckResult BuildCrossSourceCheck(CrossComparisonResult crossComparison)
    {
        if (crossComparison.FieldComparisons.Count == 0)
        {
            return AuthenticityCheckResult.NotPerformed(
                AuthenticityCheckId.CrossSourceFieldComparison, "No fields were available to cross-compare.");
        }

        var mismatched = crossComparison.FieldsRequiringReview;
        if (mismatched.Count > 0)
        {
            return new AuthenticityCheckResult(
                AuthenticityCheckId.CrossSourceFieldComparison,
                AuthenticityCheckOutcome.Failed,
                $"{mismatched.Count} field(s) disagree across independent sources: {string.Join(", ", mismatched)}.");
        }

        var matchedCount = crossComparison.FieldComparisons.Count(f => f.Status == FieldMatchStatus.Match);
        if (matchedCount == 0)
        {
            // Every field is FieldMatchStatus.Unknown - fewer than two independent sources
            // supplied a value for any field (BIO-DOC-008's own rule). This is exactly what a
            // barcode-decode-only input looks like: AC02's own scenario.
            return AuthenticityCheckResult.NotPerformed(
                AuthenticityCheckId.CrossSourceFieldComparison,
                "No field had two or more independent sources to compare against each other - a single decoded source alone (e.g. barcode only) is not sufficient for cross-source comparison (BIO-DOC-009-AC02).");
        }

        return new AuthenticityCheckResult(
            AuthenticityCheckId.CrossSourceFieldComparison,
            AuthenticityCheckOutcome.Passed,
            $"{matchedCount} field(s) agree across two or more independent sources with no conflicts.");
    }

    private static AuthenticityCheckResult BuildPassiveAuthenticationCheck(NfcCorroboration nfc)
    {
        if (!nfc.Available)
        {
            return AuthenticityCheckResult.NotPerformed(
                AuthenticityCheckId.PassiveAuthentication, "No NFC chip read was attempted or supplied.");
        }

        if (!nfc.CommunicationSucceeded)
        {
            return new AuthenticityCheckResult(
                AuthenticityCheckId.PassiveAuthentication, AuthenticityCheckOutcome.Failed, "Chip communication failed.");
        }

        if (nfc.PassiveAuthenticationPassed is null)
        {
            return AuthenticityCheckResult.NotPerformed(
                AuthenticityCheckId.PassiveAuthentication,
                "Chip communication succeeded but passive authentication was not attempted (no SOD/certificate material supplied).");
        }

        return nfc.PassiveAuthenticationPassed.Value
            ? new AuthenticityCheckResult(
                AuthenticityCheckId.PassiveAuthentication,
                AuthenticityCheckOutcome.Passed,
                "Passive authentication passed - chip data group hashes matched and the certificate chain validated.")
            : new AuthenticityCheckResult(
                AuthenticityCheckId.PassiveAuthentication, AuthenticityCheckOutcome.Failed, "Passive authentication failed.");
    }
}
