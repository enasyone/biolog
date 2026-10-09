using System.Text.Json;
using Biolog.Features.Integrations.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Biolog.Features.Integrations.Api;

/// <summary>
/// <c>/api/v1/integrations/*</c> (ADR-0004 S1.1 versioning), tenant-plane.
///
/// Preview is audit-attribution-only, no permission check (ADR-0006 Decision 2) - it never
/// mutates anything (<see cref="ImportBatchService"/>'s own header). Mapping create/update and
/// apply are this feature's own authorization decisions and each require both a permission
/// grant and a verified ADR-0006 Decision 3 provenance proof.
/// </summary>
public static class ImportEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var mappings = endpoints.MapGroup("/api/v1/integrations/mappings");
        mappings.MapPost("", CreateMappingAsync);
        mappings.MapGet("", ListMappingsAsync);
        mappings.MapGet("/{mappingId}", GetMappingAsync);
        mappings.MapPut("/{mappingId}", UpdateMappingAsync);
        mappings.MapPost("/{mappingId}/preview", PreviewAsync);
        mappings.MapPost("/{mappingId}/apply", ApplyAsync);
        mappings.MapGet("/{mappingId}/batches", ListBatchesAsync);
    }

    private static Task CreateMappingAsync(HttpContext context, ImportMappingService service, ActorProvenanceGate provenanceGate) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        provenanceGate.Require(context.Request, actor.Id);
        var request = await ReadJsonAsync<CreateMappingRequest>(context);
        var fields = ToCreateFields(request);
        var mapping = await service.CreateAsync(fields, actor, correlationId, context.RequestAborted);
        return Results.Ok(ImportSourceMappingResponse.From(mapping));
    });

    private static Task UpdateMappingAsync(HttpContext context, ImportMappingService service, ActorProvenanceGate provenanceGate, string mappingId) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        provenanceGate.Require(context.Request, actor.Id);
        var request = await ReadJsonAsync<UpdateMappingRequest>(context);
        var fields = ToUpdateFields(request);
        var mapping = await service.UpdateAsync(mappingId, fields, actor, correlationId, context.RequestAborted);
        return Results.Ok(ImportSourceMappingResponse.From(mapping));
    });

    private static Task GetMappingAsync(HttpContext context, ImportMappingService service, string mappingId) => Handle(context, async _ =>
    {
        ActorResolver.Resolve(context.Request);
        var mapping = await service.GetAsync(mappingId, context.RequestAborted) ?? throw ImportError.MappingNotFound(mappingId);
        return Results.Ok(ImportSourceMappingResponse.From(mapping));
    });

    private static Task ListMappingsAsync(HttpContext context, ImportMappingService service) => Handle(context, async _ =>
    {
        ActorResolver.Resolve(context.Request);
        var mappings = await service.ListAsync(context.RequestAborted);
        return Results.Ok(new { data = mappings.Select(ImportSourceMappingResponse.From).ToList() });
    });

    private static Task PreviewAsync(HttpContext context, ImportBatchService service, string mappingId) => Handle(context, async _ =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        var request = await ReadJsonAsync<ImportRunRequest>(context);
        var rows = ToRowInputs(request);
        var batch = await service.PreviewAsync(mappingId, rows, actor, context.RequestAborted);
        return Results.Ok(ImportBatchResponse.From(batch));
    });

    private static Task ApplyAsync(HttpContext context, ImportBatchService service, ActorProvenanceGate provenanceGate, string mappingId) => Handle(context, async correlationId =>
    {
        var actor = ActorResolver.Resolve(context.Request);
        provenanceGate.Require(context.Request, actor.Id);
        var idempotencyKey = context.Request.Headers["Idempotency-Key"].ToString();
        var request = await ReadJsonAsync<ImportRunRequest>(context);
        var rows = ToRowInputs(request);
        var (batch, replayed) = await service.ApplyAsync(mappingId, rows, idempotencyKey, actor, correlationId, context.RequestAborted);
        return Results.Ok(ImportBatchResponse.From(batch, replayed));
    });

    private static Task ListBatchesAsync(HttpContext context, ImportBatchService service, string mappingId) => Handle(context, async _ =>
    {
        ActorResolver.Resolve(context.Request);
        var batches = await service.ListBatchesAsync(mappingId, context.RequestAborted);
        return Results.Ok(new { data = batches.Select(b => ImportBatchResponse.From(b)).ToList() });
    });

    private static CreateMappingFields ToCreateFields(CreateMappingRequest request)
    {
        var errors = new List<ValidationErrorItem>();
        if (!SourceSystemTypeExtensions.TryParse(request.SourceSystem, out var sourceSystem))
        {
            errors.Add(new ValidationErrorItem("source_system", "invalid", "source_system must be one of sis, hr, payroll, erp, csv_excel"));
        }

        if (!SyncDirectionExtensions.TryParse(request.SyncDirection, out var syncDirection))
        {
            errors.Add(new ValidationErrorItem("sync_direction", "invalid", "sync_direction must be one of source_is_master, biolog_is_master"));
        }

        if (errors.Count > 0)
        {
            throw ImportError.Validation(errors);
        }

        return new CreateMappingFields(sourceSystem, syncDirection, request.DisplayName ?? "", ToFieldMappings(request.FieldMappings));
    }

    private static UpdateMappingFields ToUpdateFields(UpdateMappingRequest request)
    {
        var errors = new List<ValidationErrorItem>();
        var version = request.Version ?? 0;
        if (request.Version is null)
        {
            errors.Add(new ValidationErrorItem("version", "required", "version is required so a stale write can be detected"));
        }

        if (!SyncDirectionExtensions.TryParse(request.SyncDirection, out var syncDirection))
        {
            errors.Add(new ValidationErrorItem("sync_direction", "invalid", "sync_direction must be one of source_is_master, biolog_is_master"));
        }

        var active = request.Active ?? false;
        if (request.Active is null)
        {
            errors.Add(new ValidationErrorItem("active", "required", "active is required"));
        }

        if (errors.Count > 0)
        {
            throw ImportError.Validation(errors);
        }

        return new UpdateMappingFields(version, syncDirection, request.DisplayName ?? "", ToFieldMappings(request.FieldMappings), active);
    }

    private static IReadOnlyList<FieldMapping> ToFieldMappings(IReadOnlyList<FieldMappingRequest>? requests) =>
        requests is null ? [] : [.. requests.Select(r => new FieldMapping(r.SourceField ?? "", r.PersonField ?? ""))];

    private static IReadOnlyList<ImportRowInput> ToRowInputs(ImportRunRequest request)
    {
        if (request.Rows is null || request.Rows.Count == 0)
        {
            throw ImportError.Validation([new ValidationErrorItem("rows", "required", "at least one row is required")]);
        }

        var errors = new List<ValidationErrorItem>();
        var rows = new List<ImportRowInput>();
        foreach (var (row, index) in request.Rows.Select((r, i) => (r, i)))
        {
            if (string.IsNullOrWhiteSpace(row.ExternalKey))
            {
                errors.Add(new ValidationErrorItem($"rows[{index}].external_key", "required", "external_key is required"));
                continue;
            }

            rows.Add(new ImportRowInput(row.ExternalKey, row.Fields ?? new Dictionary<string, string?>()));
        }

        if (errors.Count > 0)
        {
            throw ImportError.Validation(errors);
        }

        return rows;
    }

    private static async Task<T> ReadJsonAsync<T>(HttpContext context)
    {
        try
        {
            var body = await JsonSerializer.DeserializeAsync<T>(context.Request.Body, ImportJsonOptions.Strict, context.RequestAborted);
            if (body is null)
            {
                throw ImportError.Validation([new ValidationErrorItem("", "required", "a JSON request body is required")]);
            }

            return body;
        }
        catch (JsonException ex)
        {
            throw ImportError.Validation([new ValidationErrorItem("", "invalid_format", $"request body is not valid JSON for this endpoint: {ex.Message}")]);
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
        catch (ImportError error)
        {
            await ProblemResponse.WriteAsync(context, error, correlationId);
        }
    }
}
