namespace Biolog.Features.Biometrics.Providers;

/// <summary>
/// A raw capture handed to a provider adapter for template extraction (e.g. a fingerprint scan
/// frame or a face capture frame). This feature never inspects <see cref="Payload"/> itself -
/// only a provider adapter that understands the modality does - so it carries no assumption
/// about image format, resolution or encoding beyond an opaque byte blob plus a declared
/// content type.
///
/// <b>BIO-BIO-001-QA-01 (Seyi round 1, High).</b> <see cref="Payload"/> clones on both
/// <c>init</c> and read: a caller that keeps its original array and mutates it after building a
/// sample cannot change what a provider actually extracts from mid-extraction, and code that
/// reads <see cref="Payload"/> back cannot reach the sample's own internal array either.
/// </summary>
public sealed record BiometricSample
{
    private readonly byte[] _payload = Array.Empty<byte>();

    public required byte[] Payload
    {
        get => (byte[])_payload.Clone();
        init => _payload = (byte[])value.Clone();
    }

    public required string ContentType { get; init; }
    public required string CapturedAtUtc { get; init; }
}
