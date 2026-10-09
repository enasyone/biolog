namespace Biolog.Features.FaceVerification.Domain;

/// <summary>Domain-level error codes this feature raises. Plain strings, not an HTTP status -
/// this project has no ASP.NET dependency, the same deferred boundary every prior BIO-BIO story
/// documents.</summary>
public sealed class FaceVerificationException : InvalidOperationException
{
    public string Code { get; }

    private FaceVerificationException(string code, string message) : base(message)
    {
        Code = code;
    }

    /// <summary>AC04's precondition failure: the claimed identity has no approved face reference
    /// to verify against at all. A genuine caller/integration error - the same treatment
    /// BIO-BIO-004's <c>FingerprintVerificationException.NoAuthorisedReference</c> gives its own
    /// missing-reference precondition - never a routine, expected outcome the way a failed
    /// comparison is.</summary>
    public static FaceVerificationException NoAuthorisedReference(string personId) =>
        new("face_verification_no_authorised_reference", $"person '{personId}' has no approved face reference to verify against.");
}
