namespace Biolog.Features.CardDesigner.Domain;

/// <summary>
/// BIO-CRD-002-AC03: "custom width/height validation exists." No specification in this
/// repository names bounds for a custom card size, so <see cref="MinEdgeMm"/>/<see cref="MaxEdgeMm"/>
/// are this story's own, deliberately named constants - one place to revise, not a magic
/// number scattered through the feature, the same resolution BIO-CRD-001 used for its own
/// unspecified DPI floor and BIO-PHO-002 used for its own unspecified capture-quality targets.
/// Chosen as a generous envelope around every standard size this story defines (CR79's 51.90mm
/// short edge to CR100's 98.50mm long edge) plus headroom for a genuinely different but still
/// card-like custom badge, while rejecting degenerate (near-zero) or physically implausible
/// (near-poster-sized) input.
/// </summary>
public static class CustomCardSizeValidator
{
    public const double MinEdgeMm = 20.0;
    public const double MaxEdgeMm = 200.0;

    public sealed record ValidationResult(bool IsValid, IReadOnlyList<string> Errors);

    public static ValidationResult Validate(double widthMm, double heightMm)
    {
        var errors = new List<string>();

        foreach (var (label, value) in new[] { ("width", widthMm), ("height", heightMm) })
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                errors.Add($"{label}_mm must be a finite number");
                continue;
            }

            if (value < MinEdgeMm || value > MaxEdgeMm)
            {
                errors.Add($"{label}_mm must be between {MinEdgeMm} and {MaxEdgeMm}");
            }
        }

        return new ValidationResult(errors.Count == 0, errors);
    }
}
