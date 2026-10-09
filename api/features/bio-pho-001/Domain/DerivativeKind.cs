namespace Biolog.Features.Photography.Domain;

/// <summary>The two derivative uses BIO-PHO-001-AC02 names: card production printing and UI/display rendering.</summary>
public enum DerivativeKind
{
    Print,
    Display,
}

public static class DerivativeKindExtensions
{
    public static string ToWireString(this DerivativeKind kind) => kind switch
    {
        DerivativeKind.Print => "print",
        DerivativeKind.Display => "display",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "unreachable: exhaustive switch"),
    };

    public static bool TryParse(string? value, out DerivativeKind kind)
    {
        switch (value)
        {
            case "print": kind = DerivativeKind.Print; return true;
            case "display": kind = DerivativeKind.Display; return true;
            default: kind = default; return false;
        }
    }
}
