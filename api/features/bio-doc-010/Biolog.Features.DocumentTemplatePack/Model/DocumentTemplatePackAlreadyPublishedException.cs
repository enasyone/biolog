namespace Biolog.Features.DocumentTemplatePack.Model;

/// <summary>Thrown by <see cref="DocumentTemplatePack.AddEntry"/> or a second
/// <see cref="DocumentTemplatePack.Publish"/> call - AC02's immutability enforced structurally, not
/// just documented.</summary>
public sealed class DocumentTemplatePackAlreadyPublishedException(string message) : InvalidOperationException(message);
