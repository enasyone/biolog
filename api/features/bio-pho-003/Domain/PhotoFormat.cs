namespace Biolog.Features.PhotoQuality.Domain;

/// <summary>The two capture formats this story supports, detected from the file's own magic bytes (<see cref="PhotoQualityRasterInspector"/>), never trusted from a caller-supplied content type - the same choice BIO-PHO-001/BIO-PHO-002 made for an identical reason.</summary>
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
