namespace Biolog.Features.Biometrics.Domain;

/// <summary>
/// Domain-level error codes <see cref="Service.BiometricService"/> raises. Plain strings, not an
/// HTTP status - this project has no ASP.NET dependency (AC03); mapping a code here to a
/// problem-response status (ADR-0004) is a later story's job once this feature has an HTTP
/// surface, the same explicitly deferred boundary BIO-DOC-001's <c>ConcurrencyException</c>
/// documents.
/// </summary>
public sealed class BiometricServiceException : InvalidOperationException
{
    public string Code { get; }

    private BiometricServiceException(string code, string message) : base(message)
    {
        Code = code;
    }

    public static BiometricServiceException ProviderUnavailable(BiometricModality modality, string providerId) =>
        new("biometric_provider_unavailable", $"provider '{providerId}' for modality '{modality}' is not healthy enough to serve a request.");

    public static BiometricServiceException NoEnrolledTemplate(string personId, BiometricModality modality) =>
        new("biometric_template_not_found", $"person '{personId}' has no enrolled '{modality}' template to verify against.");
}
