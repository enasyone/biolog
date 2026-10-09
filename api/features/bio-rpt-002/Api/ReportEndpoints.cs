using System.Text.Json;
using Biolog.Features.ReportBuilder.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Biolog.Features.ReportBuilder.Api;

/// <summary>
/// One tenant-level route group for the governed report builder itself (BIO-RPT-002-AC01-AC05),
/// entirely under its own dedicated paths. <c>/api/v1/reporting/primary-action</c> and
/// <c>/view-audit-trail</c> are deliberately NEVER mapped here: BIO-RPT-001 (Notifications,
/// already accepted) already owns those two exact paths for its own real AC05 implementation -
/// see docs/features/bio-rpt-002/README.md "AC06: connecting the existing UI" and god's ruling
/// (2026-09-24) recorded there. This story's own frontend connection uses new, dedicated
/// <c>ReportingService</c> port keys (<c>generateReport</c>/<c>getReportStatus</c>/
/// <c>cancelReport</c>/<c>exportReport</c>) pointed at the routes below - no collision with
/// <c>primaryAction</c>/<c>viewAuditTrail</c>, which stay entirely BIO-RPT-001's own.
/// </summary>
public static class ReportEndpoints
{
    private const string BasePath = "/api/v1/reporting";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(BasePath);
        group.MapGet("/schema", GetSchemaAsync);
        group.MapPost("/reports", CreateReportAsync);
        group.MapGet("/reports/{jobId}", GetReportAsync);
        group.MapPost("/reports/{jobId}/cancel", CancelReportAsync);
        group.MapGet("/reports/{jobId}/export", ExportReportAsync);
    }

    private static Task GetSchemaAsync(HttpContext context, ReportService service) => Handle(context, async _ =>
    {
        var schema = service.GetSchema();
        return Results.Ok(new ReportSchemaResponse { Datasets = [.. schema.Values.Select(ReportDatasetSchemaResponse.From)] });
    });

    private static Task CreateReportAsync(HttpContext context, ReportService service) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        var request = await ReadJsonAsync<CreateReportRequest>(context);
        var definition = ParseDefinition(request);
        var job = await service.CreateReportAsync(definition, actor, correlationId, context.RequestAborted);
        return Results.Accepted($"{BasePath}/reports/{job.JobId}", ReportJobResponse.From(job));
    });

    private static Task GetReportAsync(HttpContext context, ReportService service, string jobId) => Handle(context, async _ =>
    {
        var job = service.GetJob(jobId);
        return Results.Ok(ReportJobResponse.From(job));
    });

    private static Task CancelReportAsync(HttpContext context, ReportService service, string jobId) => Handle(context, async _ =>
    {
        var job = service.CancelJob(jobId);
        return Results.Ok(ReportJobResponse.From(job));
    });

    private static Task ExportReportAsync(HttpContext context, ReportService service, string jobId) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        var formatRaw = context.Request.Query["format"].ToString();
        if (!ReportExportFormatExtensions.TryParse(formatRaw, out var format))
        {
            throw ReportError.UnknownExportFormat(formatRaw);
        }

        var export = await service.ExportAsync(jobId, format, actor, correlationId, context.RequestAborted);
        return Results.File(export.Bytes, export.ContentType, export.FileName);
    });

    private static ReportDefinition ParseDefinition(CreateReportRequest request)
    {
        var errors = new List<ValidationErrorItem>();

        if (!ReportDatasetExtensions.TryParse(request.Dataset, out var dataset))
        {
            errors.Add(new ValidationErrorItem("dataset", "invalid", $"'{request.Dataset}' is not a recognised dataset"));
        }

        if (!ReportDimensionExtensions.TryParse(request.Dimension, out var dimension))
        {
            errors.Add(new ValidationErrorItem("dimension", "invalid", $"'{request.Dimension}' is not a recognised dimension"));
        }

        if (!ReportMeasureExtensions.TryParse(request.Measure, out var measure))
        {
            errors.Add(new ValidationErrorItem("measure", "invalid", $"'{request.Measure}' is not a recognised measure"));
        }

        if (errors.Count > 0)
        {
            throw ReportError.Validation(errors);
        }

        return new ReportDefinition { Dataset = dataset, Dimension = dimension, Measure = measure };
    }

    private static async Task<T> ReadJsonAsync<T>(HttpContext context)
    {
        try
        {
            var body = await JsonSerializer.DeserializeAsync<T>(context.Request.Body, ReportJsonOptions.Strict, context.RequestAborted);
            if (body is null)
            {
                throw ReportError.Validation([new ValidationErrorItem("", "required", "a JSON request body is required")]);
            }

            return body;
        }
        catch (JsonException ex)
        {
            throw ReportError.Validation([new ValidationErrorItem("", "invalid_format", $"request body is not valid JSON for this endpoint: {ex.Message}")]);
        }
    }

    private static async Task Handle(HttpContext context, Func<string, Task<IResult>> action)
    {
        var correlationId = CorrelationIdHeader.Resolve(context.Request);
        context.Response.Headers[CorrelationIdHeader.HeaderName] = correlationId;

        try
        {
            var result = await action(correlationId);
            await result.ExecuteAsync(context);
        }
        catch (ReportError error)
        {
            await ProblemResponse.WriteAsync(context, error, correlationId);
        }
    }
}
