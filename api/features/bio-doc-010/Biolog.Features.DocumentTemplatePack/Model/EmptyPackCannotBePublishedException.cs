namespace Biolog.Features.DocumentTemplatePack.Model;

/// <summary>Thrown by <see cref="DocumentTemplatePack.Publish"/> when the pack has zero entries -
/// a governed pack that never governs anything is not a valid publish.</summary>
public sealed class EmptyPackCannotBePublishedException(string message) : InvalidOperationException(message);
