using System.Text.Json.Serialization;
using Biolog.Features.CardDesigner.Domain;
using Microsoft.AspNetCore.Http;

namespace Biolog.Features.CardDesigner.Api;

/// <summary>ADR-0004 Decision 2 / <c>contracts/schemas/problem-response.schema.json</c>: the one HTTP error envelope, served as <c>application/problem+json</c>.</summary>
public sealed record ProblemResponse
{
    [JsonPropertyName("code")]
    public required string Code { get; init; }

    [JsonPropertyName("message")]
    public required string Message { get; init; }

    [JsonPropertyName("status")]
    public required int Status { get; init; }

    [JsonPropertyName("detail")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, object?>? Detail { get; init; }

    public static ProblemResponse From(CardDesignerError error) => new()
    {
        Code = error.Code,
        Message = error.Message,
        Status = error.Status,
        Detail = error.Detail.Count > 0 ? error.Detail : null,
    };

    public static async Task WriteAsync(HttpContext context, CardDesignerError error)
    {
        context.Response.StatusCode = error.Status;
        await context.Response.WriteAsJsonAsync(From(error), options: (System.Text.Json.JsonSerializerOptions?)null, contentType: "application/problem+json");
    }
}
