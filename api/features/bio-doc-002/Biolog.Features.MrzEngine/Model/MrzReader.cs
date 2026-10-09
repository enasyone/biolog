namespace Biolog.Features.MrzEngine.Model;

/// <summary>
/// The single public entry point for BIO-DOC-002: detection (<see cref="MrzRegionDetector"/>),
/// recognition (<see cref="MrzGlyphRecognizer"/>), parsing and check-digit validation
/// (<see cref="MrzParser"/>) as one pipeline, matching the story's own framing ("native MRZ
/// detection, recognition, parsing and validation").
/// </summary>
public static class MrzReader
{
    public static MrzReadOutcome Read(DocumentRaster raster)
    {
        var detection = MrzRegionDetector.Detect(raster);
        return MrzParser.Parse(raster, detection);
    }
}
