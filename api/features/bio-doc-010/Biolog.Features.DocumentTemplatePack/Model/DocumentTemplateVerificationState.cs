namespace Biolog.Features.DocumentTemplatePack.Model;

/// <summary>
/// The tri-state outcome of <see cref="DocumentTemplateVerification.Verify"/>, deliberately using
/// the same three names <c>web/features/documents/evidence.ts</c>'s
/// <c>EvidenceCheckState</c> vocabulary already reserves for exactly this shape of result
/// (BIO-UX-009-AC02) - <see cref="Consistent"/>/<see cref="Inconclusive"/>/<see cref="Failed"/> map
/// 1:1 onto the wire strings "consistent"/"inconclusive"/"failed" with no translation layer.
/// </summary>
public enum DocumentTemplateVerificationState
{
    /// <summary>Zero rule violations.</summary>
    Consistent,

    /// <summary>At least one violation, but only against a non-required field - not trusted
    /// unattended, routed to human review, but not an outright rejection.</summary>
    Inconclusive,

    /// <summary>At least one violation against a required field.</summary>
    Failed,
}

public static class DocumentTemplateVerificationStateExtensions
{
    public static string ToWireString(this DocumentTemplateVerificationState state) => state switch
    {
        DocumentTemplateVerificationState.Consistent => "consistent",
        DocumentTemplateVerificationState.Inconclusive => "inconclusive",
        DocumentTemplateVerificationState.Failed => "failed",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "unreachable: exhaustive switch"),
    };
}
