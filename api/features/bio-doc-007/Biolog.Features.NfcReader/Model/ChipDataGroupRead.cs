using System.Security.Cryptography;

namespace Biolog.Features.NfcReader.Model;

/// <summary>
/// One successfully read data group (BIO-DOC-007-AC02). Deliberately carries a SHA-256 hash of
/// the raw bytes, never the bytes themselves - Dg1 (MRZ) and Dg2 (encoded face) are directly
/// identity PII, the same "hash, not payload" reasoning BIO-DOC-003's <c>BarcodeDecodeResult</c>
/// and BIO-DOC-001's <c>DocumentEvidence</c> already apply (ADR-0003 Decision 5: "evidence
/// references, not evidence copies"). <see cref="PassiveAuthentication.Evaluate"/> compares this
/// hash against the hash the Document Security Object declares for the same data group - the hash
/// is exactly what passive authentication needs, never the raw content.
///
/// <para>
/// Every accessor is <c>internal init</c> with an explicit <c>internal</c> constructor, the same
/// reasoning as every prior BIO-DOC-00x engine result type: a caller must not be able to fabricate
/// a read that never actually happened via <c>with</c>.
/// </para>
/// </summary>
public sealed record ChipDataGroupRead
{
    public ChipDataGroupId Id { get; internal init; }
    public string Sha256Hash { get; internal init; } = null!;
    public int LengthBytes { get; internal init; }

    internal ChipDataGroupRead(ChipDataGroupId id, string sha256Hash, int lengthBytes)
    {
        Id = id;
        Sha256Hash = sha256Hash;
        LengthBytes = lengthBytes;
    }

    public static ChipDataGroupRead FromRawBytes(ChipDataGroupId id, byte[] rawBytes)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(rawBytes));
        return new ChipDataGroupRead(id, hash, rawBytes.Length);
    }
}
