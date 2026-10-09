namespace Biolog.Features.FaceEnrolment.Domain;

/// <summary>Domain-level error codes this feature raises. Plain strings, not an HTTP status -
/// this project has no ASP.NET dependency, the same deferred boundary BIO-BIO-001/002 document.</summary>
public sealed class FaceEnrolmentException : InvalidOperationException
{
    public string Code { get; }

    private FaceEnrolmentException(string code, string message) : base(message)
    {
        Code = code;
    }

    public static FaceEnrolmentException SourcePhotoNotFound(string personId, int photoMasterVersion) =>
        new("face_enrolment_source_photo_not_found", $"person '{personId}' has no photo master version {photoMasterVersion} (AC03 source-photo linkage).");

    public static FaceEnrolmentException ReferenceNotFound(string referenceId) =>
        new("face_biometric_reference_not_found", $"reference '{referenceId}' does not exist.");
}
