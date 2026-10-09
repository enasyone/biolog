namespace Biolog.Features.DocumentVerify.Model;

/// <summary>
/// A normalized (0.0-1.0, fraction of image width/height) axis-aligned bounding box locating
/// where a <see cref="DocumentFieldEvidence"/> value was read from on its source image
/// (BIO-DOC-001-AC03). Normalized rather than pixel coordinates so a box remains meaningful
/// regardless of the resolution the evidence image was captured or later re-encoded at.
/// </summary>
public sealed record BoundingBox
{
    // QA-03 (Chioma acceptance audit): `private init` is what makes `Create` mandatory - see
    // DocumentEvidence's remarks for the full reasoning. The acceptance probe demonstrated this
    // exact type being built directly with negative/NaN/zero/>1 components via object initializer.
    public double X { get; private init; }
    public double Y { get; private init; }
    public double Width { get; private init; }
    public double Height { get; private init; }

    // See DocumentEvidence's identical private constructor for why this is required, not
    // cosmetic - without it `new BoundingBox()` still compiles from outside this type and
    // produces (0, 0, 0, 0), which this type's own Create rejects (width/height must be > 0)
    // but the implicit constructor does not check at all.
    private BoundingBox()
    {
    }

    public static BoundingBox Create(double x, double y, double width, double height)
    {
        // QA-02 (Seyi review): NaN fails every "<"/">"/"<="/">=" comparison, so the range and
        // sum checks below silently accept it (and every other non-finite value) unless it is
        // rejected explicitly first - a NaN/Infinity coordinate cannot define a source-image
        // region.
        RequireFinite(x, nameof(x));
        RequireFinite(y, nameof(y));
        RequireFinite(width, nameof(width));
        RequireFinite(height, nameof(height));

        RequireUnitRange(x, nameof(x));
        RequireUnitRange(y, nameof(y));
        if (width <= 0 || width > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "must be in (0, 1]");
        }
        if (height <= 0 || height > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(height), height, "must be in (0, 1]");
        }
        if (x + width > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "x + width must not exceed 1");
        }
        if (y + height > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(height), height, "y + height must not exceed 1");
        }

        return new BoundingBox { X = x, Y = y, Width = width, Height = height };
    }

    private static void RequireUnitRange(double value, string paramName)
    {
        if (value < 0 || value >= 1)
        {
            throw new ArgumentOutOfRangeException(paramName, value, "must be in [0, 1)");
        }
    }

    private static void RequireFinite(double value, string paramName)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(paramName, value, "must be a finite number");
        }
    }
}
