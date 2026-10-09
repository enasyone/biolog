namespace Biolog.Features.NfcReader.Model;

/// <summary>
/// The derived encryption/MAC key pair used to establish a secure channel with a chip
/// (BIO-DOC-007-AC01). Produced only via <see cref="ChipAccessKeyDerivation.DeriveKeys"/> - the
/// same lockdown reasoning as every prior BIO-DOC-00x trust-bearing result type, applied here to
/// key material rather than a confidence/validity flag: a caller must not be able to substitute
/// forged key bytes that never actually went through the ICAO 9303 key-derivation function,
/// defeating the point of deriving them from permitted data in the first place.
///
/// <para>
/// <b>BIO-DOC-007-QA-02 (Chioma's acceptance review):</b> an <c>internal init</c> accessor on a
/// <c>byte[]</c> property only blocks REASSIGNING the property (via <c>with</c>, blocked by
/// accessibility) - it does nothing to stop a caller mutating the ARRAY'S CONTENTS in place
/// through the public getter, since arrays are reference types and the getter used to return the
/// same mutable array every internal storage held. <c>keys.EncryptionKey[0] = 0xFF;</c> compiled
/// and silently corrupted the key every subsequent caller would see. Fixed by storing the bytes in
/// private fields never exposed directly, defensively copying on both construction (so mutating an
/// array the caller passed into <see cref="Create"/> after the call cannot reach the stored key
/// either) and on every property access (so a caller mutating what a getter returned can never
/// affect the stored key or any other caller's own copy). <see cref="EncryptionKey"/>/
/// <see cref="MacKey"/> are now get-only - no setter of any kind exists for <c>with</c> to target,
/// a strictly stronger lockdown than the previous accessibility-only restriction.
/// </para>
/// </summary>
public sealed record NfcAccessKeys
{
    private readonly byte[] _encryptionKey;
    private readonly byte[] _macKey;

    public byte[] EncryptionKey => (byte[])_encryptionKey.Clone();
    public byte[] MacKey => (byte[])_macKey.Clone();

    internal NfcAccessKeys(byte[] encryptionKey, byte[] macKey)
    {
        _encryptionKey = (byte[])encryptionKey.Clone();
        _macKey = (byte[])macKey.Clone();
    }

    internal static NfcAccessKeys Create(byte[] encryptionKey, byte[] macKey) => new(encryptionKey, macKey);
}
