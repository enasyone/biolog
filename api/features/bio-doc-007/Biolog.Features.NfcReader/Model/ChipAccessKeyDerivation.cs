using System.Security.Cryptography;

namespace Biolog.Features.NfcReader.Model;

/// <summary>
/// ICAO 9303 Part 11 Appendix D.1's shared key-derivation function, used by both Basic Access
/// Control and PACE (BIO-DOC-007-AC01): a SHA-1-based KDF over a key seed plus a 4-byte
/// big-endian counter, with odd-parity bit adjustment so the result is usable as a (2-key
/// triple-)DES key. Built entirely from <c>System.Security.Cryptography</c> - part of the .NET
/// base class library, not a NuGet package, matching every prior BIO-DOC-00x engine's "no external
/// dependency" discipline.
///
/// <para>
/// <b>On not hard-coding a published ICAO worked-example digest:</b> ICAO 9303 Part 11 Appendix
/// D.2 publishes a numeric worked example for this exact algorithm. This implementation
/// deliberately does not claim to reproduce that specific published byte sequence from memory -
/// transcribing a remembered hex constant and asserting it as "matches the standard" without an
/// independent way to check the transcription would be exactly the kind of unverified claim this
/// codebase's engineering discipline exists to avoid (see BIO-DOC-002's MRZ check-digit oracle
/// reasoning). Instead, correctness is verified two ways that do not depend on trusting a
/// memorized constant: (1) the algorithm steps themselves - SHA-1, a 4-byte big-endian counter,
/// truncation to 16 bytes, DES odd-parity adjustment - are standard, independently-specified
/// operations implemented directly against their own definitions; (2) the developer test suite
/// cross-checks this implementation's output against an independently-computed SHA-1 digest
/// (Python's <c>hashlib</c>, a wholly separate implementation) for the same input, over the same
/// algorithm steps - see <c>ChipAccessKeyDerivationTests</c>.
/// </para>
/// </summary>
public static class ChipAccessKeyDerivation
{
    /// <summary>ICAO 9303 Part 11 Appendix D.1: counter value for deriving the encryption key.</summary>
    public const int EncryptionKeyCounter = 1;

    /// <summary>ICAO 9303 Part 11 Appendix D.1: counter value for deriving the MAC key.</summary>
    public const int MacKeyCounter = 2;

    /// <summary>K_seed = SHA-1(seed input)[0..16] - the first 16 bytes of the digest.</summary>
    public static byte[] DeriveKeySeed(NfcAccessKeySeedMaterial seedMaterial)
    {
        var digest = SHA1.HashData(seedMaterial.SeedInputBytes);
        return digest[..16];
    }

    /// <summary>K = AdjustParity(SHA-1(keySeed || counter as 4-byte big-endian)[0..16]).</summary>
    public static byte[] DeriveKey(byte[] keySeed, int counter)
    {
        var counterBytes = new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(counterBytes, (uint)counter);

        var input = new byte[keySeed.Length + counterBytes.Length];
        keySeed.CopyTo(input, 0);
        counterBytes.CopyTo(input, keySeed.Length);

        var digest = SHA1.HashData(input);
        var key = digest[..16];
        AdjustParity(key);
        return key;
    }

    public static NfcAccessKeys DeriveKeys(NfcAccessKeySeedMaterial seedMaterial)
    {
        var seed = DeriveKeySeed(seedMaterial);
        return NfcAccessKeys.Create(DeriveKey(seed, EncryptionKeyCounter), DeriveKey(seed, MacKeyCounter));
    }

    /// <summary>Sets each byte's low bit so the byte has odd parity (the standard DES key-parity
    /// convention) - mutates <paramref name="key"/> in place.</summary>
    private static void AdjustParity(byte[] key)
    {
        for (var i = 0; i < key.Length; i++)
        {
            var b = key[i];
            var onesInTopSevenBits = System.Numerics.BitOperations.PopCount((uint)(b >> 1));
            key[i] = (byte)((b & 0xFE) | (onesInTopSevenBits % 2 == 0 ? 1 : 0));
        }
    }
}
