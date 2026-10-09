namespace Biolog.Features.PhotoBooth.Domain;

/// <summary>The two capture formats this story supports (matches BIO-PHO-001's own PhotoFormat) - a photo booth capture, never a vector asset. Detected from the file's own magic bytes (<see cref="CaptureRasterInspector"/>), never trusted from a caller-supplied content type.</summary>
public enum CaptureFormat
{
    Jpeg,
    Png,
}

public static class CaptureFormatExtensions
{
    public static string ToWireString(this CaptureFormat format) => format switch
    {
        CaptureFormat.Jpeg => "jpeg",
        CaptureFormat.Png => "png",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "unreachable: exhaustive switch"),
    };
}
