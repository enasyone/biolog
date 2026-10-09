namespace Biolog.Features.CardProduction.Api;

/// <summary>One entry in a problem response's <c>errors[]</c> (ADR-0004 Decision 5.3).</summary>
public sealed record ValidationErrorItem(string Path, string Code, string Message);
