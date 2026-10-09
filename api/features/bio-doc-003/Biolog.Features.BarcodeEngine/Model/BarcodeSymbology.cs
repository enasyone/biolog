namespace Biolog.Features.BarcodeEngine.Model;

/// <summary>The symbologies this service supports "at minimum" (BIO-DOC-003-AC01) through
/// registered, replaceable <see cref="IBarcodeDecoder"/> implementations.</summary>
public enum BarcodeSymbology
{
    Qr,
    Pdf417,
    DataMatrix,
    Aztec,
    Code128,
    Code39,
}
