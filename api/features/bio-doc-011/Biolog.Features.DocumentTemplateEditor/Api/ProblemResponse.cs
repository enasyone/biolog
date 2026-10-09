using System.Text.Json.Serialization;
using Biolog.Features.DocumentTemplateEditor.Domain;
using Microsoft.AspNetCore.Http;

namespace Biolog.Features.DocumentTemplateEditor.Api;

/// <summary>
/// ADR-0004 Decision 2 / <c>contracts/schemas/problem-response.schema.json</c>: the one HTTP
/// error envelope for the whole platform, served as <c>application/problem+json</c>. A local,
/// schema-conformant implementation scoped to this feature's own error type
/// (<see cref="DocumentTemplateDraftError"/>), the same pattern every prior HTTP-surfaced BIOLOG
/// feature already uses.
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

    public static ProblemResponse From(DocumentTemplateDraftError error, string correlationId) => new()
    {
        Code = error.Code,
        Message = error.Message,
        Status = error.Status,
        CorrelationId = correlationId,
        Detail = error.Detail.Count > 0 ? error.Detail : null,
    };

    public static async Task WriteAsync(HttpContext context, DocumentTemplateDraftError error, string correlationId)
    {
        context.Response.StatusCode = error.Status;
        context.Response.Headers[CorrelationIdHeader.HeaderName] = correlationId;
        await context.Response.WriteAsJsonAsync(From(error, correlationId), options: (System.Text.Json.JsonSerializerOptions?)null, contentType: "application/problem+json");
    }
}
