namespace Biolog.Features.DocumentTemplatePack.Model;

/// <summary>Thrown by <see cref="DocumentTemplateVerification.Verify"/> against a pack still in
/// <see cref="DocumentTemplatePackStatus.Draft"/> - a verification records a PUBLISHED pack's
/// version (AC04); a draft has no governance standing yet.</summary>
public sealed class PackNotPublishedException(string message) : InvalidOperationException(message);

/// <summary>Thrown by <see cref="DocumentTemplateVerification.Verify"/> when the named entry id is
/// not part of the given pack - governance requires verifying against an entry the pack actually
/// declares, never an arbitrary caller-supplied id.</summary>
public sealed class EntryNotInPackException(string message) : InvalidOperationException(message);
