namespace Biolog.Features.Photography.Domain;

/// <summary>
/// The two capture formats this story supports (BIO-PHO-001-AC01's own scope: a photo booth
/// or an enrolment station capture, never a vector asset - unlike BIO-CRD-001's brand-asset
/// library, there is no SVG case here). Detected from the file's own magic bytes
/// (<see cref="PhotoFormatDetector"/>), never trusted from a caller-supplied content type.
/// </summary>
public enum PhotoFormat
{
    Jpeg,
    Png,
}

public static class PhotoFormatExtensions
{
    public static string ToWireString(this PhotoFormat format) => format switch
    {
        PhotoFormat.Jpeg => "jpeg",
        PhotoFormat.Png => "png",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "unreachable: exhaustive switch"),
    };
}
