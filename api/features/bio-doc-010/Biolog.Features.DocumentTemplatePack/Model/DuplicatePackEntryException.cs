namespace Biolog.Features.DocumentTemplatePack.Model;

/// <summary>Thrown when <see cref="DocumentTemplatePack.AddEntry"/> is given an
/// <see cref="DocumentTemplatePackEntry.EntryId"/> already present in this pack - an entry is added
/// once, never replaced in place (see <see cref="DocumentTemplatePackEntry"/>'s own immutability
/// remarks).</summary>
public sealed class DuplicatePackEntryException(string message) : InvalidOperationException(message);
