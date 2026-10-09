namespace Biolog.Features.TenantDomainRegistry.Api;

/// <summary>
/// One entry in a problem response's <c>errors[]</c> (ADR-0004 Decision 5.3 /
/// <c>contracts/schemas/validation-error-item.schema.json</c>). <see cref="Path"/> is a flat
/// field name here - every request DTO in this feature has no nested objects, so the
/// dot-escaping rule <c>escapeSegment</c> exists for is a no-op this feature never exercises;
/// it is not reimplemented for that reason, not because the rule was overlooked.
/// </summary>
public sealed record ValidationErrorItem(string Path, string Code, string Message);
