namespace Biolog.Features.DocumentTemplatePack.Model;

/// <summary>
/// AC01's NFC metadata for one governed pack entry - descriptive, not a second chip reader
/// (BIO-DOC-007 is not a dependency of this story). <see cref="Supported"/> is what BIO-OPS-005's
/// own acceptance criterion ("unsupported NFC is not treated as pass/fail authenticity") reads to
/// decide whether a missing NFC signal for this document identity is expected, not a failure.
/// </summary>
public sealed record NfcMetadata(bool Supported, string? ChipStandard, string? Note)
{
    public static NfcMetadata NotSupported(string note) => new(false, null, note);
}
