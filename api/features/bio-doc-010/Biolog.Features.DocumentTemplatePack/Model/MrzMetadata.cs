namespace Biolog.Features.DocumentTemplatePack.Model;

/// <summary>
/// AC01's MRZ metadata for one governed pack entry - descriptive, not a second MRZ engine
/// (BIO-DOC-002 is deliberately not a dependency of this story; <see cref="FormatId"/> is a
/// loosely-coupled string, the same "reference by id, not by type" discipline
/// <c>DocumentIntelligenceRegistryEntry.TemplateId</c> already established for BIO-DOC-004).
/// </summary>
public sealed record MrzMetadata(bool Supported, string? FormatId, string? Note)
{
    public static MrzMetadata NotSupported(string note) => new(false, null, note);
}
