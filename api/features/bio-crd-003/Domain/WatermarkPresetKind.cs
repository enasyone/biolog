namespace Biolog.Features.CardTypography.Domain;

/// <summary>BIO-CRD-003-AC05: "STAFF/STUDENT/VISITOR starter watermark behaviors are available." The three fixed starter categories this story names - no AC asks for a tenant-configurable catalogue.</summary>
public enum WatermarkPresetKind
{
    Staff,
    Student,
    Visitor,
}

public static class WatermarkPresetKindExtensions
{
    public static string ToWireString(this WatermarkPresetKind kind) => kind switch
    {
        WatermarkPresetKind.Staff => "staff",
        WatermarkPresetKind.Student => "student",
        WatermarkPresetKind.Visitor => "visitor",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "unreachable: exhaustive switch"),
    };

    public static bool TryParse(string? value, out WatermarkPresetKind kind)
    {
        switch (value)
        {
            case "staff": kind = WatermarkPresetKind.Staff; return true;
            case "student": kind = WatermarkPresetKind.Student; return true;
            case "visitor": kind = WatermarkPresetKind.Visitor; return true;
            default: kind = default; return false;
        }
    }
}
