namespace Biolog.Features.DocumentTemplateEditor.Api;

/// <summary>One entry in a problem response's <c>errors[]</c> (ADR-0004 Decision 5.3). Mirrors BIO-CRD-008's own identical type.</summary>
public sealed record ValidationErrorItem(string Path, string Code, string Message);
