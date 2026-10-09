using System.Text.Json;
using Biolog.Features.CardProduction.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Biolog.Features.CardProduction.Api;

/// <summary>
/// Two route groups (ADR-0004 §1.1 versioning): job/package/activation actions nested under the
/// owning card issue, matching this epic's own established convention, and one tenant-level
/// route for AC03's offline-sync intake (the token itself carries which card it is for).
/// <c>Activate</c> and <c>OfflineComplete</c> both require <c>Idempotency-Key</c> (ADR-0004
/// §4.3 names card activation and offline sync intake explicitly).
/// </summary>
public static class ProductionEndpoints
{
    public const string IdempotencyKeyHeaderName = "Idempotency-Key";
    private const string CardBasePath = "/api/v1/persons/{personId}/card-issues/{cardClass}/{sequenceNumber:int}/production";
    private const string TenantBasePath = "/api/v1/card-production";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var cardGroup = endpoints.MapGroup(CardBasePath);
        cardGroup.MapPost("/jobs", CreateJobAsync);
        cardGroup.MapGet("/jobs", ListJobsAsync);
        cardGroup.MapGet("/jobs/{jobSequenceNumber:int}", GetJobAsync);
        cardGroup.MapPost("/jobs/{jobSequenceNumber:int}/steps/print", RecordPrintAsync);
        cardGroup.MapPost("/jobs/{jobSequenceNumber:int}/steps/encode", RecordEncodeAsync);
        cardGroup.MapPost("/jobs/{jobSequenceNumber:int}/steps/readback-qc", RecordReadBackQcAsync);
        cardGroup.MapPost("/jobs/{jobSequenceNumber:int}/reject", RejectJobAsync);
        cardGroup.MapPost("/jobs/{jobSequenceNumber:int}/cancel", CancelJobAsync);
        cardGroup.MapPost("/packages", IssuePackageAsync);
        cardGroup.MapPost("/activate", ActivateAsync);

        var tenantGroup = endpoints.MapGroup(TenantBasePath);
        tenantGroup.MapPost("/offline-complete", CompleteOfflineJobAsync);
    }

    private static Task CreateJobAsync(HttpContext context, ProductionService service, string personId, string cardClass, int sequenceNumber) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        var request = await ReadJsonAsync<CreateJobRequest>(context);
        var job = await service.CreateJobAsync(personId, cardClass, sequenceNumber, request.PrinterRef, request.WorkstationRef, request.ReprintReason, actor, correlationId, context.RequestAborted);
        return Results.Created($"{CardBasePath.Replace("{personId}", personId).Replace("{cardClass}", cardClass).Replace("{sequenceNumber:int}", sequenceNumber.ToString())}/jobs/{job.JobSequenceNumber}", ProductionJobResponse.From(job));
    });

    private static Task ListJobsAsync(HttpContext context, ProductionService service, string personId, string cardClass, int sequenceNumber) => Handle(context, async _ =>
    {
        var jobs = await service.ListJobsAsync(personId, cardClass, sequenceNumber, context.RequestAborted);
        return Results.Ok(new ProductionJobListResponse { Data = [.. jobs.Select(ProductionJobResponse.From)] });
    });

    private static Task GetJobAsync(HttpContext context, ProductionService service, string personId, string cardClass, int sequenceNumber, int jobSequenceNumber) => Handle(context, async _ =>
    {
        var job = await service.GetJobAsync(personId, cardClass, sequenceNumber, jobSequenceNumber, context.RequestAborted)
            ?? throw ProductionError.JobNotFound(personId, cardClass, sequenceNumber, jobSequenceNumber);
        return Results.Ok(ProductionJobResponse.From(job));
    });

    private static Task RecordPrintAsync(HttpContext context, ProductionService service, string personId, string cardClass, int sequenceNumber, int jobSequenceNumber) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        var request = await ReadJsonAsync<StepRequest>(context);
        var job = await service.RecordPrintAsync(personId, cardClass, sequenceNumber, jobSequenceNumber, request.Passed, request.Detail, request.ExpectedVersion, actor, correlationId, context.RequestAborted);
        return Results.Ok(ProductionJobResponse.From(job));
    });

    private static Task RecordEncodeAsync(HttpContext context, ProductionService service, string personId, string cardClass, int sequenceNumber, int jobSequenceNumber) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        var request = await ReadJsonAsync<StepRequest>(context);
        var job = await service.RecordEncodeAsync(personId, cardClass, sequenceNumber, jobSequenceNumber, request.Passed, request.Detail, request.ExpectedVersion, actor, correlationId, context.RequestAborted);
        return Results.Ok(ProductionJobResponse.From(job));
    });

    private static Task RecordReadBackQcAsync(HttpContext context, ProductionService service, string personId, string cardClass, int sequenceNumber, int jobSequenceNumber) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        var request = await ReadJsonAsync<StepRequest>(context);
        var job = await service.RecordReadBackQcAsync(personId, cardClass, sequenceNumber, jobSequenceNumber, request.Passed, request.Detail, request.ExpectedVersion, actor, correlationId, context.RequestAborted);
        return Results.Ok(ProductionJobResponse.From(job));
    });

    private static Task RejectJobAsync(HttpContext context, ProductionService service, string personId, string cardClass, int sequenceNumber, int jobSequenceNumber) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        var request = await ReadJsonAsync<RejectRequest>(context);
        var job = await service.RejectJobAsync(personId, cardClass, sequenceNumber, jobSequenceNumber, request.Reason, request.ExpectedVersion, actor, correlationId, context.RequestAborted);
        return Results.Ok(ProductionJobResponse.From(job));
    });

    private static Task CancelJobAsync(HttpContext context, ProductionService service, string personId, string cardClass, int sequenceNumber, int jobSequenceNumber) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        var request = await ReadJsonAsync<VersionedRequest>(context);
        var job = await service.CancelJobAsync(personId, cardClass, sequenceNumber, jobSequenceNumber, request.ExpectedVersion, actor, correlationId, context.RequestAborted);
        return Results.Ok(ProductionJobResponse.From(job));
    });

    private static Task IssuePackageAsync(HttpContext context, ProductionService service, string personId, string cardClass, int sequenceNumber) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        var request = await ReadJsonAsync<IssuePackageRequest>(context);
        var ttl = request.TtlMinutes is int minutes ? TimeSpan.FromMinutes(minutes) : (TimeSpan?)null;
        var (package, token) = await service.IssuePackageAsync(personId, cardClass, sequenceNumber, request.PrinterRef, request.WorkstationRef, request.ReprintReason, ttl, actor, correlationId, context.RequestAborted);
        return Results.Ok(ProductionPackageResponse.From(package, token));
    });

    private static Task ActivateAsync(HttpContext context, ProductionService service, string personId, string cardClass, int sequenceNumber) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        var request = await ReadJsonAsync<VersionedRequest>(context);
        var idempotencyKey = context.Request.Headers[IdempotencyKeyHeaderName].ToString();
        var snapshot = await service.ActivateAfterProductionAsync(personId, cardClass, sequenceNumber, request.ExpectedVersion, idempotencyKey, actor, correlationId, context.RequestAborted);
        return Results.Ok(CardIssueSnapshotResponse.From(snapshot));
    });

    private static Task CompleteOfflineJobAsync(HttpContext context, ProductionService service) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        var request = await ReadJsonAsync<OfflineCompleteRequest>(context);
        var idempotencyKey = context.Request.Headers[IdempotencyKeyHeaderName].ToString();
        var job = await service.CompleteOfflineJobAsync(
            request.Token, request.PrintPassed, request.PrintDetail, request.EncodePassed, request.EncodeDetail, request.ReadBackQcPassed, request.ReadBackQcDetail,
            idempotencyKey, actor, correlationId, context.RequestAborted);
        return Results.Ok(ProductionJobResponse.From(job));
    });

    private static async Task<T> ReadJsonAsync<T>(HttpContext context)
    {
        try
        {
            var body = await JsonSerializer.DeserializeAsync<T>(context.Request.Body, ProductionJsonOptions.Strict, context.RequestAborted);
            if (body is null)
            {
                throw ProductionError.Validation([new ValidationErrorItem("", "required", "a JSON request body is required")]);
            }

            return body;
        }
        catch (JsonException ex)
        {
            throw ProductionError.Validation([new ValidationErrorItem("", "invalid_format", $"request body is not valid JSON for this endpoint: {ex.Message}")]);
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
        catch (ProductionError error)
        {
            await ProblemResponse.WriteAsync(context, error, correlationId);
        }
    }
}
