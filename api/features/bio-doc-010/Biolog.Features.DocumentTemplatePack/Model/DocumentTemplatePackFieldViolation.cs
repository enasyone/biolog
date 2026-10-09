namespace Biolog.Features.DocumentTemplatePack.Model;

/// <summary>One <see cref="FieldValidationRule"/> an observed value failed - either the field was
/// required and absent, or present but did not match the rule's pattern.</summary>
public sealed record DocumentTemplatePackFieldViolation(string FieldName, string Reason);
