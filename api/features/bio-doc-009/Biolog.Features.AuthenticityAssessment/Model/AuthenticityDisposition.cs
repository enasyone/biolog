namespace Biolog.Features.AuthenticityAssessment.Model;

/// <summary>
/// The overall policy result BIO-DOC-009-AC01 requires alongside the individual checks.
/// <see cref="DocumentAuthentic"/> is the literal "DOCUMENT AUTHENTIC" outcome AC02 names -
/// <see cref="AuthenticityPolicyEngine"/> is the only place this value is ever produced, and it is
/// structurally unreachable from a barcode-decode-only input (see that type's remarks).
/// <see cref="Inconclusive"/> is the safe default when evidence is simply insufficient to decide
/// either way (e.g. every check <see cref="AuthenticityCheckOutcome.NotPerformed"/>) - never
/// silently promoted to <see cref="DocumentAuthentic"/> for lack of a Failed check.
/// </summary>
public enum AuthenticityDisposition
{
    DocumentAuthentic,
    DocumentNotAuthentic,
    Inconclusive,
}
