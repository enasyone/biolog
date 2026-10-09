namespace Biolog.Features.Integrations.Api;

/// <summary>One entry in a problem response's <c>errors[]</c> (ADR-0004 Decision 5.3 / <c>contracts/schemas/validation-error-item.schema.json</c>).</summary>
public sealed record ValidationErrorItem(string Path, string Code, string Message);
