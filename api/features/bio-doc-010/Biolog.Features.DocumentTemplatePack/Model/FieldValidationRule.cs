namespace Biolog.Features.DocumentTemplatePack.Model;

/// <summary>
/// AC01's "validation rules": one rule constraining a single declared <see cref="DocumentTemplatePackEntry.ExpectedFields"/>
/// entry. <see cref="Pattern"/> is an optional regular expression an observed field value must
/// match; <see cref="Required"/> states whether the field must be present at all.
/// <see cref="DocumentTemplateVerification.Verify"/> is the only code that evaluates these.
/// </summary>
public sealed record FieldValidationRule(string FieldName, bool Required, string? Pattern);
