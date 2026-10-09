namespace Biolog.Features.CardDesigner.Domain;

/// <summary>BIO-CRD-002-AC01: the three standard physical card blank sizes this story names, plus a caller-supplied custom size (AC03).</summary>
public enum CardFormatKind
{
    Cr80,
    Cr79,
    Cr100,
    Custom,
}

public static class CardFormatKindExtensions
{
    public static string ToWireString(this CardFormatKind kind) => kind switch
    {
        CardFormatKind.Cr80 => "cr80",
        CardFormatKind.Cr79 => "cr79",
        CardFormatKind.Cr100 => "cr100",
        CardFormatKind.Custom => "custom",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "unreachable: exhaustive switch"),
    };

    public static bool TryParse(string? value, out CardFormatKind kind)
    {
        switch (value)
        {
            case "cr80": kind = CardFormatKind.Cr80; return true;
            case "cr79": kind = CardFormatKind.Cr79; return true;
            case "cr100": kind = CardFormatKind.Cr100; return true;
            case "custom": kind = CardFormatKind.Custom; return true;
            default: kind = default; return false;
        }
    }
}
