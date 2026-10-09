using System.Text.Json.Serialization;

namespace Biolog.Features.OfflineSync.Api;

/// <summary><c>contracts/schemas/validation-error-item.schema.json</c> (ADR-0004 Decision 5.3).</summary>
public sealed record ValidationErrorItem(
    [property: JsonPropertyName("field")] string Field,
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message);
