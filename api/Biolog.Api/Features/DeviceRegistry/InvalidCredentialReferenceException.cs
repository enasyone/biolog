namespace Biolog.Api.Features.DeviceRegistry;

/// <summary>Thrown by <see cref="CredentialReferenceValidator"/> when a caller-supplied
/// credential reference is not a well-formed pointer into a supported secret store
/// (BIO-DEV-001-QA-04).</summary>
public sealed class InvalidCredentialReferenceException(string message) : Exception(message);
