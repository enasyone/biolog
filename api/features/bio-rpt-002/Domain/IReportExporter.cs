namespace Biolog.Features.ReportBuilder.Domain;

public sealed record ReportExport(byte[] Bytes, string ContentType, string FileName);

/// <summary>One export hook per format (BIO-RPT-002-AC02). A format with no live renderer throws <see cref="ReportError.ExportFormatNotImplemented"/> rather than being absent from <see cref="ReportExporterRegistry"/> - the hook exists and is reachable, it is just honestly unfinished, matching AC06's explicit "or a documented HYBRID state" allowance.</summary>
public interface IReportExporter
{
    ReportExportFormat Format { get; }

    ReportExport Export(ReportResult result, string reportName);
}

/// <summary>
/// CSV is the one format that actually renders in this foundation story - no third-party
/// library is referenced anywhere in this repository's .csproj/NuGet configuration yet (verified
/// before writing this), and CSV needs none: RFC 4180 quoting for a comma/quote/newline is a
/// handful of lines. Excel/PDF each need a real rendering library this offline build cannot
/// restore from NuGet - see <see cref="UnimplementedReportExporter"/> and
/// docs/features/bio-rpt-002/README.md for the disclosed HYBRID gap this leaves.
/// </summary>
public sealed class CsvReportExporter : IReportExporter
{
    public ReportExportFormat Format => ReportExportFormat.Csv;

    public ReportExport Export(ReportResult result, string reportName)
    {
        // RFC 4180 §2.1 mandates CRLF line endings regardless of host platform - StringBuilder's
        // own AppendLine uses Environment.NewLine, which is "\n" on the Linux/macOS containers
        // this codebase actually runs on, silently producing an RFC-non-compliant file (caught
        // by this exporter's own developer test, not assumed correct).
        var builder = new System.Text.StringBuilder();
        builder.Append(string.Join(',', result.Columns.Select(QuoteCsvField))).Append("\r\n");
        foreach (var row in result.Rows)
        {
            builder.Append(string.Join(',', row.Select(QuoteCsvField))).Append("\r\n");
        }

        var bytes = System.Text.Encoding.UTF8.GetBytes(builder.ToString());
        return new ReportExport(bytes, "text/csv", $"{reportName}.csv");
    }

    private static string QuoteCsvField(string field)
    {
        if (field.IndexOfAny([',', '"', '\n', '\r']) < 0)
        {
            return field;
        }

        return $"\"{field.Replace("\"", "\"\"")}\"";
    }
}

/// <summary>Excel/PDF (BIO-RPT-002-AC02's disclosed HYBRID gap - see <see cref="CsvReportExporter"/>'s own header).</summary>
public sealed class UnimplementedReportExporter(ReportExportFormat format) : IReportExporter
{
    public ReportExportFormat Format { get; } = format;

    public ReportExport Export(ReportResult result, string reportName) => throw ReportError.ExportFormatNotImplemented(Format);
}

public static class ReportExporterRegistry
{
    public static readonly IReadOnlyDictionary<ReportExportFormat, IReportExporter> Exporters = new Dictionary<ReportExportFormat, IReportExporter>
    {
        [ReportExportFormat.Csv] = new CsvReportExporter(),
        [ReportExportFormat.Excel] = new UnimplementedReportExporter(ReportExportFormat.Excel),
        [ReportExportFormat.Pdf] = new UnimplementedReportExporter(ReportExportFormat.Pdf),
    };
}
