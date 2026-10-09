using System.Text.Json.Serialization;
using Biolog.Features.TenantDomainRegistry.Domain;
using Microsoft.AspNetCore.Http;

namespace Biolog.Features.TenantDomainRegistry.Api;

/// <summary>
/// ADR-0004 Decision 2 / <c>contracts/schemas/problem-response.schema.json</c>: the one HTTP
/// error envelope for the whole platform, served as <c>application/problem+json</c>. This is
/// a local, schema-conformant implementation scoped to this feature's own error type
/// (<see cref="TenantDomainError"/>) - duck-typing onto a shared <c>toProblemResponse</c> is
/// the contracts.README.md rule 7 ideal, but no .NET binding exists yet to duck-type onto.
/// Every field name, requiredness and constraint below is taken directly from the schema, not
/// invented.
/// </summary>
public sealed record ProblemResponse
{
    [JsonPropertyName("code")]
    public required string Code { get; init; }

    [JsonPropertyName("message")]
    public required string Message { get; init; }

    [JsonPropertyName("status")]
    public required int Status { get; init; }

    [JsonPropertyName("correlation_id")]
    public required string CorrelationId { get; init; }

    [JsonPropertyName("detail")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, object?>? Detail { get; init; }

    [JsonPropertyName("errors")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<ValidationErrorItem>? Errors { get; init; }

    public static ProblemResponse From(TenantDomainError error, string correlationId) => new()
    {
        Code = error.Code,
        Message = error.Message,
        Status = error.Status,
        CorrelationId = correlationId,
        // "errors is present only for a validation failure ... never present alongside a
        // non-validation code" (ADR-0004 §2.1) - Detail is likewise omitted whenever Errors
        // is present, since a validation_failed error carries no additional detail object.
        Detail = error.Errors is null && error.Detail.Count > 0 ? error.Detail : null,
        Errors = error.Errors,
    };

    public static async Task WriteAsync(HttpContext context, TenantDomainError error, string correlationId)
    {
        context.Response.StatusCode = error.Status;
        context.Response.Headers[CorrelationIdHeader.HeaderName] = correlationId;
        // WriteAsJsonAsync sets its own Content-Type unless told otherwise, which would
        // silently overwrite "application/problem+json" (ADR-0004 §2.1) with the default
        // "application/json" - the contentType parameter, not a prior ContentType
        // assignment, is what actually wins.
        await context.Response.WriteAsJsonAsync(From(error, correlationId), options: (System.Text.Json.JsonSerializerOptions?)null, contentType: "application/problem+json");
    }
}
