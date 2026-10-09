// Developer tests for BIO-RPT-002. SYNTHETIC fixtures only.
using System.Text;
using Biolog.Features.ReportBuilder.Domain;
using Xunit;

namespace Biolog.Features.ReportBuilder.Tests;

public sealed class CsvReportExporterTests
{
    [Fact]
    public void AC02_renders_a_real_RFC4180_csv_with_header_and_rows()
    {
        var result = new ReportResult
        {
            Columns = ["status", "count"],
            Rows = [["passed", "2"], ["failed", "1"]],
            SourceRecordCount = 3,
            GeneratedAt = "2026-09-24T00:00:00.000Z",
        };
        var exporter = new CsvReportExporter();

        var export = exporter.Export(result, "report-card-production");

        Assert.Equal("text/csv", export.ContentType);
        Assert.Equal("report-card-production.csv", export.FileName);
        var text = Encoding.UTF8.GetString(export.Bytes);
        Assert.Equal("status,count\r\npassed,2\r\nfailed,1\r\n", text);
    }

    [Fact]
    public void AC02_quotes_a_field_containing_a_comma_quote_or_newline()
    {
        var result = new ReportResult
        {
            Columns = ["status", "count"],
            Rows = [["needs \"quoting\", really", "1"]],
            SourceRecordCount = 1,
            GeneratedAt = "2026-09-24T00:00:00.000Z",
        };
        var exporter = new CsvReportExporter();

        var export = exporter.Export(result, "report");
        var text = Encoding.UTF8.GetString(export.Bytes);

        Assert.Contains("\"needs \"\"quoting\"\", really\",1", text);
    }

    [Theory]
    [InlineData(ReportExportFormat.Excel)]
    [InlineData(ReportExportFormat.Pdf)]
    public void AC02_excel_and_pdf_hooks_exist_and_answer_honestly_that_they_are_not_yet_implemented(ReportExportFormat format)
    {
        var exporter = ReportExporterRegistry.Exporters[format];
        var result = new ReportResult { Columns = ["status", "count"], Rows = [], SourceRecordCount = 0, GeneratedAt = "2026-09-24T00:00:00.000Z" };

        var error = Assert.Throws<ReportError>(() => exporter.Export(result, "report"));

        Assert.Equal("report_export_format_not_implemented", error.Code);
        Assert.Equal(501, error.Status);
    }
}
