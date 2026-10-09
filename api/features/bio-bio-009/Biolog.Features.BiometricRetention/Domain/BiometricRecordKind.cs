namespace Biolog.Features.BiometricRetention.Domain;

/// <summary>
/// The two enrolled-record kinds this story's retention/deletion policy governs. A local type,
/// not BIO-BIO-001's own <c>BiometricModality</c> - this project takes no ProjectReference to any
/// sibling BIO-BIO story (see this project's csproj remarks).
/// </summary>
internal enum BiometricRecordKind
{
    FingerprintSession,
    FaceReference,
}

/// <summary>
/// AC01's "category" - the thing a retention policy is configured per, per tenant. Broader than
/// <see cref="BiometricRecordKind"/>: <see cref="RawImage"/> has no record-kind counterpart (a
/// raw image is an optional attachment on either kind's own record, never its own store), and is
/// governed separately by <see cref="IRawImageRetentionPolicy"/> (AC03) rather than a day count
/// alone.
/// </summary>
internal enum RetentionCategory
{
    FingerprintTemplate,
    FaceTemplate,
    RawImage,
}
