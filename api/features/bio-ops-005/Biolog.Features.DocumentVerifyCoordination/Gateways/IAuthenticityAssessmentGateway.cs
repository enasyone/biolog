using Biolog.Features.EvidenceComparison.Model;

namespace Biolog.Features.DocumentVerifyCoordination.Gateways;

/// <summary>
/// Kemi's BIO-OPS-005 ruling, point 3: a structural check's outcome for one of this coordinator's
/// own evidence steps, in a plain shape neither BIO-DOC-009 nor BIO-DOC-010 needs a
/// <c>ProjectReference</c> back to define - the identical "plain outcome+detail pair" precedent
/// <c>AuthenticityAssessmentInputs</c>'s own doc comment already established for MRZ/barcode/OCR.
/// </summary>
public enum EngineCheckOutcome
{
    NotPerformed,
    Passed,
    Failed,
    Inconclusive,
}

public sealed record EngineCheckInput(EngineCheckOutcome Outcome, string Detail);

/// <summary>Already projected onto <c>web/features/documents/evidence.ts</c>'s own
/// <c>EvidenceCheckState</c> wire vocabulary ("authentic" | "inconclusive" | "failed") by the
/// adapter that implements this gateway - this coordinator never needs to know BIO-DOC-009's own
/// <c>AuthenticityDisposition</c> enum shape to report a step result.</summary>
public sealed record AuthenticityAssessmentSnapshot(string EvidenceCheckState, double Confidence, string Summary);

/// <summary>
/// This feature's own narrow seam onto BIO-DOC-009's real, already-registered
/// <c>AuthenticityAssessmentService</c> - deliberately NOT a <c>ProjectReference</c> to
/// <c>api/features/bio-doc-009</c> (Kemi's ruling, point 2). The real implementation is an
/// adapter in <c>Biolog.Api.Modules.BioOps005Module</c> wrapping that service's new
/// <c>ComputeFromEvidence</c> overload directly - the same <c>ICardIssueGateway</c> shape every
/// prior cross-story seam in this epic already established.
/// </summary>
public interface IAuthenticityAssessmentGateway
{
    Task<AuthenticityAssessmentSnapshot> ComputeFromEvidenceAsync(
        string documentSessionId,
        EngineCheckInput mrz,
        EngineCheckInput barcode,
        EngineCheckInput ocr,
        CrossComparisonResult crossComparison,
        string actorId,
        string correlationId,
        CancellationToken cancellationToken = default);
}
