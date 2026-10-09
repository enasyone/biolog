namespace Biolog.Features.AuthenticityAssessment.Model;

/// <summary>
/// ADR-0003's "structured, never a bare string" discipline applied to this story's own enums -
/// mirrors <c>ActorTypeExtensions.ToWireString()</c> (this project's own <c>Audit/AuditPrimitives.cs</c>).
/// Explicit mapping rather than <c>.ToString()</c> so a future rename of an enum member can never
/// silently change this API's wire contract.
/// </summary>
public static class AuthenticityCheckIdExtensions
{
    public static string ToWireString(this AuthenticityCheckId id) => id switch
    {
        AuthenticityCheckId.MrzStructuralValidity => "mrz_structural_validity",
        AuthenticityCheckId.BarcodeDecode => "barcode_decode",
        AuthenticityCheckId.OcrFieldExtraction => "ocr_field_extraction",
        AuthenticityCheckId.DocumentClassification => "document_classification",
        AuthenticityCheckId.CrossSourceFieldComparison => "cross_source_field_comparison",
        AuthenticityCheckId.PassiveAuthentication => "passive_authentication",
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, "unreachable: exhaustive switch"),
    };
}

public static class AuthenticityCheckOutcomeExtensions
{
    public static string ToWireString(this AuthenticityCheckOutcome outcome) => outcome switch
    {
        AuthenticityCheckOutcome.NotPerformed => "not_performed",
        AuthenticityCheckOutcome.Passed => "passed",
        AuthenticityCheckOutcome.Failed => "failed",
        AuthenticityCheckOutcome.Inconclusive => "inconclusive",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "unreachable: exhaustive switch"),
    };
}

public static class AuthenticityDispositionExtensions
{
    public static string ToWireString(this AuthenticityDisposition disposition) => disposition switch
    {
        AuthenticityDisposition.DocumentAuthentic => "document_authentic",
        AuthenticityDisposition.DocumentNotAuthentic => "document_not_authentic",
        AuthenticityDisposition.Inconclusive => "inconclusive",
        _ => throw new ArgumentOutOfRangeException(nameof(disposition), disposition, "unreachable: exhaustive switch"),
    };

    public static bool TryParse(string? value, out AuthenticityDisposition disposition)
    {
        switch (value)
        {
            case "document_authentic": disposition = AuthenticityDisposition.DocumentAuthentic; return true;
            case "document_not_authentic": disposition = AuthenticityDisposition.DocumentNotAuthentic; return true;
            case "inconclusive": disposition = AuthenticityDisposition.Inconclusive; return true;
            default: disposition = default; return false;
        }
    }

    /// <summary>
    /// Maps to <c>web/features/documents/evidence.ts</c>'s <c>EvidenceCheckState</c> vocabulary
    /// (BIO-UX-009-AC02, "read, structurally valid, consistent, authentic, failed, not-checked and
    /// inconclusive") for BIO-DOC-009-AC06's live wire response. <c>DocumentAuthentic</c> maps to
    /// the vocabulary's own literal <c>'authentic'</c> state - the more precise choice now that a
    /// real policy disposition exists to name it after (the SIMULATION-mode mock adapter's own
    /// choice of <c>'consistent'</c> for this step predates this story's policy vocabulary; see
    /// docs/features/bio-doc-009/AUTHENTICITY-ORCHESTRATOR.md for the full reasoning - SIMULATION
    /// mode is unaffected, only this LIVE mapping).
    /// </summary>
    public static string ToEvidenceCheckState(this AuthenticityDisposition disposition) => disposition switch
    {
        AuthenticityDisposition.DocumentAuthentic => "authentic",
        AuthenticityDisposition.DocumentNotAuthentic => "failed",
        AuthenticityDisposition.Inconclusive => "inconclusive",
        _ => throw new ArgumentOutOfRangeException(nameof(disposition), disposition, "unreachable: exhaustive switch"),
    };
}
