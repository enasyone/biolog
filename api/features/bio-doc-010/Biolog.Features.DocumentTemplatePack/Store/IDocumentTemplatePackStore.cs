namespace Biolog.Features.DocumentTemplatePack.Store;

/// <summary>
/// Durable-in-intent store of every <see cref="Model.DocumentTemplatePack"/> version this process
/// has ever published - never overwritten in place (ADR-0001 S3.3): a new pack version is a new
/// entry, and every previously published version stays retrievable by its own
/// (<see cref="Model.DocumentTemplatePack.PackId"/>, <see cref="Model.DocumentTemplatePack.PackVersion"/>).
/// </summary>
public interface IDocumentTemplatePackStore
{
    /// <summary>Adds a pack to the store. Throws <see cref="DuplicatePackVersionException"/> if
    /// this exact (PackId, PackVersion) pair is already present - a pack version is recorded
    /// once, never replaced.</summary>
    void Save(Model.DocumentTemplatePack pack);

    /// <summary>The current (highest <see cref="Model.DocumentTemplatePack.PackVersion"/> among
    /// PUBLISHED packs) pack for a given <see cref="Model.DocumentTemplatePack.CountryScope"/>, or
    /// <c>null</c> if none has been published yet.</summary>
    Model.DocumentTemplatePack? GetCurrentPublished(string countryScope);

    Model.DocumentTemplatePack? GetByVersion(string packId, string packVersion);

    /// <summary>Resolves the PUBLISHED pack whose own <see cref="Model.DocumentTemplatePack.FindEntryByTemplateId"/>
    /// names <paramref name="templateId"/> as one of its entries' sides - AC04's own
    /// "which pack governs this template" lookup.</summary>
    (Model.DocumentTemplatePack Pack, Model.DocumentTemplatePackEntry Entry)? FindByTemplateId(string templateId);
}

/// <summary>Thrown by <see cref="IDocumentTemplatePackStore.Save"/> when this exact
/// (PackId, PackVersion) pair already exists in the store.</summary>
public sealed class DuplicatePackVersionException(string message) : InvalidOperationException(message);
