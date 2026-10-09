using Biolog.Features.DocumentTemplatePack.Model;

namespace Biolog.Features.DocumentTemplatePack.Store;

/// <summary>In-process store for every <see cref="Model.DocumentTemplatePack"/> version this
/// process has published - the same seam every prior BIOLOG feature's own in-memory store uses; a
/// real durable store is a later story's job.</summary>
public sealed class InMemoryDocumentTemplatePackStore : IDocumentTemplatePackStore
{
    private readonly object _gate = new();
    private readonly Dictionary<(string PackId, string PackVersion), Model.DocumentTemplatePack> _byKey = [];

    public void Save(Model.DocumentTemplatePack pack)
    {
        ArgumentNullException.ThrowIfNull(pack);

        lock (_gate)
        {
            var key = (pack.PackId, pack.PackVersion);
            if (_byKey.ContainsKey(key))
            {
                throw new DuplicatePackVersionException(
                    $"pack '{pack.PackId}' version '{pack.PackVersion}' is already recorded in this store");
            }

            _byKey[key] = pack;
        }
    }

    public Model.DocumentTemplatePack? GetCurrentPublished(string countryScope)
    {
        lock (_gate)
        {
            return _byKey.Values
                .Where(p => p.CountryScope == countryScope && p.Status == DocumentTemplatePackStatus.Published)
                .OrderByDescending(p => p.PublishedAt, StringComparer.Ordinal)
                .FirstOrDefault();
        }
    }

    public Model.DocumentTemplatePack? GetByVersion(string packId, string packVersion)
    {
        lock (_gate)
        {
            return _byKey.GetValueOrDefault((packId, packVersion));
        }
    }

    public (Model.DocumentTemplatePack Pack, DocumentTemplatePackEntry Entry)? FindByTemplateId(string templateId)
    {
        lock (_gate)
        {
            foreach (var pack in _byKey.Values.Where(p => p.Status == DocumentTemplatePackStatus.Published))
            {
                var entry = pack.FindEntryByTemplateId(templateId);
                if (entry is not null)
                {
                    return (pack, entry);
                }
            }

            return null;
        }
    }
}
