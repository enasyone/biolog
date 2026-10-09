namespace Biolog.Features.AuthenticityAssessment.Model;

/// <summary>
/// The individual checks BIO-DOC-009-AC01 requires the assessment to report ("individual checks
/// and overall policy result"), grouped as the user story names them: structural
/// (<see cref="MrzStructuralValidity"/>, <see cref="BarcodeDecode"/>, <see cref="OcrFieldExtraction"/>,
/// <see cref="DocumentClassification"/>), cross-source (<see cref="CrossSourceFieldComparison"/>),
/// and cryptographic (<see cref="PassiveAuthentication"/>).
///
/// <see cref="BarcodeDecode"/> is named explicitly, not folded into a generic "structural" bucket,
/// because AC02 ("barcode decode alone can never produce DOCUMENT AUTHENTIC") requires a caller to
/// be able to see it as its own reported check - see <see cref="AuthenticityPolicyEngine"/>'s
/// remarks for how the policy enforces this structurally, not just by convention.
/// </summary>
public enum AuthenticityCheckId
{
    MrzStructuralValidity,
    BarcodeDecode,
    OcrFieldExtraction,
    DocumentClassification,
    CrossSourceFieldComparison,
    PassiveAuthentication,
}
