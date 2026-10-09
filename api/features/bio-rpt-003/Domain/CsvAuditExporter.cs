using System.Text;
using Biolog.Features.AuditInvestigation.Audit;

namespace Biolog.Features.AuditInvestigation.Domain;

/// <summary>
/// BIO-RPT-003-AC03's export. Hand-rolled, RFC 4180 CRLF line endings regardless of host platform
/// - <c>StringBuilder.AppendLine</c> uses <c>Environment.NewLine</c> ("\n" on the Linux/macOS
/// containers this codebase runs on), the exact bug BIO-RPT-002's own CSV exporter caught in its
/// own developer test; that lesson is applied here from the start rather than re-discovered.
/// </summary>
public static class CsvAuditExporter
{
    private static readonly string[] Header =
    [
        "event_id", "source_feature", "tenant_id", "actor_type", "actor_id", "person_id", "action",
        "target_type", "target_id", "outcome", "reason_code", "occurred_at", "classification", "withheld", "detail",
    ];

    public static byte[] Export(IReadOnlyList<NormalizedAuditRecord> records)
    {
        var builder = new StringBuilder();
        builder.Append(string.Join(',', Header.Select(Quote))).Append("\r\n");

        foreach (var record in records)
        {
            var withheld = string.Join(';', record.Withheld);
            var detail = string.Join(';', record.Detail.Select(kv => $"{kv.Key}={kv.Value}"));
            var fields = new[]
            {
                record.EventId,
                record.SourceFeature,
                record.TenantId,
                record.Actor.Type.ToWireString(),
                record.Actor.Id,
                record.PersonId ?? string.Empty,
                record.Action,
                record.Target.Type,
                record.Target.Id ?? string.Empty,
                record.Outcome.ToWireString(),
                record.ReasonCode,
                record.OccurredAt,
                record.Classification,
                withheld,
                detail,
            };
            builder.Append(string.Join(',', fields.Select(Quote))).Append("\r\n");
        }

        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    private static string Quote(string field)
    {
        if (field.IndexOfAny([',', '"', '\n', '\r']) < 0)
        {
            return field;
        }

        return $"\"{field.Replace("\"", "\"\"")}\"";
    }
}
