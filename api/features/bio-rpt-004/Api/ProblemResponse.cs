using System.Text.Json.Serialization;
using Biolog.Features.OperationalDashboard.Domain;
using Microsoft.AspNetCore.Http;

namespace Biolog.Features.OperationalDashboard.Api;

/// <summary>ADR-0004 Decision 2: the one HTTP error envelope, served as <c>application/problem+json</c>.</summary>
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

    public static ProblemResponse From(DashboardError error, string correlationId) => new()
    {
        Code = error.Code,
        Message = error.Message,
        Status = error.Status,
        CorrelationId = correlationId,
        Detail = error.Detail.Count > 0 ? error.Detail : null,
    };

    public static async Task WriteAsync(HttpContext context, DashboardError error, string correlationId)
    {
        context.Response.StatusCode = error.Status;
        context.Response.Headers[CorrelationIdHeader.HeaderName] = correlationId;
        await context.Response.WriteAsJsonAsync(From(error, correlationId), options: (System.Text.Json.JsonSerializerOptions?)null, contentType: "application/problem+json");
    }
}
