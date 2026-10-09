using System.Text.Json.Serialization;
using Biolog.Features.PersonRegistry.Domain;
using Microsoft.AspNetCore.Http;

namespace Biolog.Features.PersonRegistry.Api;

/// <summary>
/// ADR-0004 Decision 2 / <c>contracts/schemas/problem-response.schema.json</c>: the one HTTP
/// error envelope for the whole platform, served as <c>application/problem+json</c>. A local,
/// schema-conformant implementation scoped to this feature's own error type
/// (<see cref="PersonError"/>) - no .NET binding exists yet to duck-type onto.
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

    public static ProblemResponse From(PersonError error, string correlationId) => new()
    {
        Code = error.Code,
        Message = error.Message,
        Status = error.Status,
        CorrelationId = correlationId,
        Detail = error.Errors is null && error.Detail.Count > 0 ? error.Detail : null,
        Errors = error.Errors,
    };

    public static async Task WriteAsync(HttpContext context, PersonError error, string correlationId)
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
