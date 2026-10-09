using Biolog.Features.DocumentTemplatePack.Model;

namespace Biolog.Features.DocumentTemplatePack.Store;

/// <summary>Governed store of <see cref="DocumentTemplateSample"/>s (AC03) - accepts a sample only
/// once its <see cref="DocumentTemplateSample.EntryId"/> is confirmed to name a real entry in the
/// given pack.</summary>
public interface IDocumentTemplateSampleStore
{
    /// <summary>Throws <see cref="UngovernedSampleEntryException"/> if <paramref name="sample"/>'s
    /// <see cref="DocumentTemplateSample.EntryId"/> is not one of <paramref name="pack"/>'s own
    /// entries.</summary>
    void Add(Model.DocumentTemplatePack pack, DocumentTemplateSample sample);

    IReadOnlyList<DocumentTemplateSample> GetForEntry(string entryId);
}

/// <summary>Thrown when a sample names an <see cref="DocumentTemplateSample.EntryId"/> the given
/// pack does not govern.</summary>
public sealed class UngovernedSampleEntryException(string message) : InvalidOperationException(message);
