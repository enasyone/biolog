using Biolog.Features.ReportBuilder.Identifiers;

namespace Biolog.Features.ReportBuilder.Domain;

/// <summary>
/// The governed aggregation engine itself (BIO-RPT-002-AC01, AC03). Every read goes through
/// <see cref="ICardProductionGateway"/>/<see cref="IAttendanceGateway"/>, tenant-scoped by
/// construction (the caller passes <c>tenantId</c>; there is no code path that can read a
/// second tenant's records within one call) - never a raw store or arbitrary query.
///
/// Cooperative cancellation (AC04): checked once before the gateway read and once per batch of
/// <see cref="CancellationCheckBatchSize"/> records while grouping, so a genuinely large result
/// set gives <see cref="ReportJobRunner"/>'s cancel path real, repeated windows to observe a
/// cancellation request - not just a single check at the very start.
/// </summary>
public sealed class ReportGenerationService(ICardProductionGateway cardProductionGateway, IAttendanceGateway attendanceGateway)
{
    internal const int CancellationCheckBatchSize = 25;

    public async Task<ReportResult> GenerateAsync(string tenantId, ReportDefinition definition, CancellationToken cancellationToken = default)
    {
        ReportDefinitionValidator.Validate(definition);
        cancellationToken.ThrowIfCancellationRequested();

        var values = definition.Dataset switch
        {
            ReportDataset.CardProduction => await FetchCardProductionAsync(tenantId, definition.Dimension, cancellationToken),
            ReportDataset.Attendance => await FetchAttendanceAsync(tenantId, definition.Dimension, cancellationToken),
            _ => throw ReportError.UnknownDataset(definition.Dataset),
        };

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var processed = 0;
        foreach (var value in values)
        {
            counts[value] = counts.GetValueOrDefault(value) + 1;
            processed += 1;
            if (processed % CancellationCheckBatchSize == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
            }
        }

        return new ReportResult
        {
            Columns = [definition.Dimension.ToWireString(), definition.Measure.ToWireString()],
            Rows = [.. counts.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => (IReadOnlyList<string>)[kv.Key, kv.Value.ToString()])],
            SourceRecordCount = values.Count,
            GeneratedAt = UtcInstant.Now(),
        };
    }

    private async Task<IReadOnlyList<string>> FetchCardProductionAsync(string tenantId, ReportDimension dimension, CancellationToken cancellationToken)
    {
        var records = await cardProductionGateway.ListAsync(tenantId, cancellationToken);
        return [.. records.Select(r => dimension switch
        {
            ReportDimension.Status => r.Status,
            ReportDimension.Source => r.Source,
            ReportDimension.CardClass => r.CardClass,
            _ => throw ReportError.DimensionNotWhitelisted(ReportDataset.CardProduction, dimension),
        })];
    }

    private async Task<IReadOnlyList<string>> FetchAttendanceAsync(string tenantId, ReportDimension dimension, CancellationToken cancellationToken)
    {
        var records = await attendanceGateway.ListAsync(tenantId, cancellationToken);
        return [.. records.Select(r => dimension switch
        {
            ReportDimension.Mode => r.Mode,
            ReportDimension.Source => r.Source,
            ReportDimension.VerificationMethod => r.VerificationMethod,
            _ => throw ReportError.DimensionNotWhitelisted(ReportDataset.Attendance, dimension),
        })];
    }
}
