namespace Biolog.Features.ReportBuilder.Domain;

/// <summary>BIO-RPT-002-AC02: "CSV/Excel/PDF export hooks exist." Every format is a real, named case a caller can request and get a real (CSV) or honestly-documented-not-yet-live (Excel/PDF) response for - never a format the API pretends not to know about.</summary>
public enum ReportExportFormat
{
    Csv,
    Excel,
    Pdf,
}

public static class ReportExportFormatExtensions
{
    public static bool TryParse(string? value, out ReportExportFormat format)
    {
        switch (value?.ToLowerInvariant())
        {
            case "csv": format = ReportExportFormat.Csv; return true;
            case "excel": format = ReportExportFormat.Excel; return true;
            case "pdf": format = ReportExportFormat.Pdf; return true;
            default: format = default; return false;
        }
    }
}
