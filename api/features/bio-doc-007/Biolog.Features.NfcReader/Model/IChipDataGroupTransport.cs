namespace Biolog.Features.NfcReader.Model;

/// <summary>
/// The replaceable, standards-focused abstraction a real NFC/ISO 7816 driver implements
/// (BIO-DOC-007-AC02) - the same "BIOLOG owns the standard/orchestration, low-level hardware
/// access is replaceable" shape BIO-DOC-003's <c>IBarcodeDecoder</c> established for barcode
/// symbol decoding. No implementation of this interface ships in this story: BAC/PACE's actual
/// challenge-response or Diffie-Hellman/ECDH handshake, and the ISO 7816 APDU exchange needed to
/// read a data group off a physical chip, both require real hardware this codebase has no driver
/// for (the same "no image-decoding library exists, so recognition works from an already-captured
/// raster" boundary every BIO-DOC-00x engine before this one has kept, applied here to NFC
/// transport instead of a camera capture).
/// </summary>
public interface IChipDataGroupTransport
{
    /// <summary>Attempts to negotiate a secure channel using the supplied access keys (BAC's
    /// mutual-authentication challenge-response, or PACE's key-agreement handshake). Returns
    /// <c>false</c> on ANY failure to establish the channel - a wrong/rejected key, an
    /// unresponsive chip, a dropped connection - without distinguishing the cause at this layer;
    /// <see cref="ChipReadOutcome.ReadFailureReason"/> is where a transport-specific reason, if
    /// any, is surfaced (BIO-DOC-007-AC05).</summary>
    bool EstablishSecureChannel(NfcAccessKeys keys);

    /// <summary>Attempts to read one data group's raw bytes over the already-established secure
    /// channel. Returns <c>false</c> (not an exception) when the data group is simply not present
    /// on this chip or not accessible under the currently-established access level - Dg3/Dg4 being
    /// unavailable under BAC-only access is an ordinary, expected outcome, not a transport
    /// failure.</summary>
    bool TryReadDataGroup(ChipDataGroupId id, out byte[]? rawBytes);
}
