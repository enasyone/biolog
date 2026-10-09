namespace Biolog.Features.FingerprintVerification.Domain;

/// <summary>Domain-level error codes this feature raises. Plain strings, not an HTTP status -
/// this project has no ASP.NET dependency, the same deferred boundary BIO-BIO-001/002/003
/// document.</summary>
public sealed class FingerprintVerificationException : InvalidOperationException
{
    public string Code { get; }

    private FingerprintVerificationException(string code, string message) : base(message)
    {
        Code = code;
    }

    /// <summary>AC01's precondition failure: the claimed identity has nothing enrolled to verify
    /// against at all. A genuine caller/integration error - the same treatment BIO-BIO-003's
    /// <c>FaceEnrolmentException.SourcePhotoNotFound</c> gives a missing upstream precondition -
    /// never a routine, expected outcome the way a failed comparison (AC03) is.</summary>
    public static FingerprintVerificationException NoAuthorisedReference(string personId) =>
        new("fingerprint_verification_no_authorised_reference",
            $"person '{personId}' has no authorised fingerprint reference to verify against.");
}
