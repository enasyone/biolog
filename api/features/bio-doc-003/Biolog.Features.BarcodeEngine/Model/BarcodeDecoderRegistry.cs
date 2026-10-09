namespace Biolog.Features.BarcodeEngine.Model;

/// <summary>
/// Registered <see cref="IBarcodeDecoder"/> instances, one per symbology (BIO-DOC-003-AC01).
/// Pre-populates all six required symbologies with this story's own native decoders;
/// <see cref="Register"/> is the seam a later story uses to swap any one of them out (e.g. for a
/// platform-native or higher-fidelity decoder) without touching
/// <see cref="BarcodeDetectionService"/> - the same "configured, not hard-coded" pattern
/// BIO-DOC-002's <c>MrzFormatRegistry</c> established for MRZ formats.
/// </summary>
public sealed class BarcodeDecoderRegistry
{
    private readonly Dictionary<BarcodeSymbology, IBarcodeDecoder> _decoders = [];

    public IReadOnlyCollection<IBarcodeDecoder> All => _decoders.Values;

    /// <summary>Registers a decoder, replacing any prior one already registered for the same
    /// symbology - the explicit "swap the implementation" seam AC01 asks for.</summary>
    public void Register(IBarcodeDecoder decoder) => _decoders[decoder.Symbology] = decoder;

    public IBarcodeDecoder? Get(BarcodeSymbology symbology) => _decoders.GetValueOrDefault(symbology);

    /// <summary>The registry this story ships with: this story's own native decoder for every
    /// required symbology (BIO-DOC-003-AC01), ready for any of them to be individually replaced.</summary>
    public static BarcodeDecoderRegistry CreateDefault()
    {
        var registry = new BarcodeDecoderRegistry();
        registry.Register(new Code39Decoder());
        registry.Register(new Code128SignatureDecoder());
        registry.Register(new QrSignatureDecoder());
        registry.Register(new DataMatrixSignatureDecoder());
        registry.Register(new AztecSignatureDecoder());
        registry.Register(new Pdf417SignatureDecoder());
        return registry;
    }
}
