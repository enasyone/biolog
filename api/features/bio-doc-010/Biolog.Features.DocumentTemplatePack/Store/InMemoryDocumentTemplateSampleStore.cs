using Biolog.Features.DocumentTemplatePack.Model;

namespace Biolog.Features.DocumentTemplatePack.Store;

/// <summary>In-process governed sample store - the same seam every prior BIOLOG feature's own
/// in-memory store uses.</summary>
public sealed class InMemoryDocumentTemplateSampleStore : IDocumentTemplateSampleStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, List<DocumentTemplateSample>> _byEntryId = [];

    public void Add(Model.DocumentTemplatePack pack, DocumentTemplateSample sample)
    {
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentNullException.ThrowIfNull(sample);

        if (pack.FindEntry(sample.EntryId) is null)
        {
            throw new UngovernedSampleEntryException(
                $"sample '{sample.SampleId}' names entry '{sample.EntryId}', which pack '{pack.PackId}' version '{pack.PackVersion}' does not govern");
        }

        lock (_gate)
        {
            if (!_byEntryId.TryGetValue(sample.EntryId, out var list))
            {
                list = [];
                _byEntryId[sample.EntryId] = list;
            }

            list.Add(sample);
        }
    }

    public IReadOnlyList<DocumentTemplateSample> GetForEntry(string entryId)
    {
        lock (_gate)
        {
            return _byEntryId.TryGetValue(entryId, out var list) ? [.. list] : [];
        }
    }
}
